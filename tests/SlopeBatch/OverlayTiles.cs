// T253 slope overlay (SkiSlopeOverlayTiles): the one-source-of-truth proof and the cache / strip-run invariants. CoreCLR only (Mono bit
// equality is unverified, as for the classifier).
// (1) Parity: a ring rated by SkiRunRater and the overlay tiles on the same terrain. Every inside cell whose full 20 m window fits the
//     ring's Mask has a tile value bit-identical to the ring's CellGrade, and CellValue on the ring's own grid reproduces CellGrade
//     for every inside cell (the extraction did not change the rating).
// (2) FadeLevel steps. (3) BuildRuns covers every disk cell once, merges maximally, honors the cap and the skip, and a stride halves the
//     row count. (4) Verify: a height change invalidates the tiles whose padded heights hold it, and the recompute equals a fresh cache.
// (5) Eviction keeps the cap and never drops a tile of the current disk. (6) TIME lines: a cold 300 m disk and one BuildRuns.
// The section runs with the rating fingerprint and the minimax oracle off, so the fingerprint stays comparable.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using SkiResortMod.Systems;
using Unity.Mathematics;

namespace SlopeBatch
{
    internal static partial class Program
    {
        private struct SkipBox : ISlopeCellSkip
        {
            public int I0, I1, J0, J1;
            public int Asked;

            public bool Skip(int i, int j)
            {
                Asked++;
                return i >= I0 && i <= I1 && j >= J0 && j <= J1;
            }
        }

        private static HeightFn OverlaySteep()
        {
            return (x, z) => 1600.0 - 0.38 * x + 70.0 * Math.Sin(x / 85.0) * Math.Cos(z / 120.0) + 12.0 * Math.Sin(x / 31.0 + z / 17.0)
                             + 25.0 * Math.Cos(z / 60.0);
        }

        private static void OverlayTilesTests()
        {
            bool fp = s_FingerprintOn, oracle = s_OracleOn;
            s_FingerprintOn = false;
            s_OracleOn = false;
            try
            {
                OverlayParity("mountain", Mountain());
                OverlayParity("steep", OverlaySteep());
                OverlayFade();
                OverlayRuns();
                OverlayVerify();
                OverlayEvict();
                OverlayTiming();
            }
            finally
            {
                s_FingerprintOn = fp;
                s_OracleOn = oracle;
            }
        }

        private static Terrain OverlayTerrain(HeightFn f, double cx, double cz, double radius)
        {
            double m = radius + 200.0;
            return Terrain.Build(f, cx - m, cz - m, cx + m, cz + m);
        }

        private static void OverlayParity(string name, HeightFn f)
        {
            string w = "overlay/parity " + name;
            double2[] ring = Rect(380, 260, Frame.Make(120, -40, 23));
            Terrain t = OverlayTerrain(f, 120, -40, 450);
            var ts = new TerrainSampler { T = t };
            var grid = new SlopeGrid();
            var res = new SlopeResult();
            float3[] pts = Pts(ring, false);
            RateStatus rs = SkiRunRater.Rate(pts, pts.Length, Terrain.Origin, ref ts, Terrain.Texel, 0, grid, null, res, out int m,
                out float _, out float _);
            if (!Check(rs == RateStatus.Rated && m == 1, w, "rate " + rs + " m " + m + " (need m = 1)"))
                return;

            double rcx = 0, rcz = 0;
            foreach (double2 p in ring)
            {
                rcx += p.x / ring.Length;
                rcz += p.y / ring.Length;
            }
            var tiles = new SkiSlopeOverlayTiles();
            Check(tiles.SetLattice(Terrain.Origin, Terrain.Texel), w, "SetLattice refused the test lattice");
            int missing = tiles.Update(ref ts, rcx, rcz, 300f, 1, 1e9f);
            Check(missing == 0, w, missing + " tiles missing after an unbounded update");

            var cp = SkiSlopeClassifier.CellParams.For(grid.Step);
            int inside = 0, fullCells = 0, mismatch = 0, refactor = 0, noTile = 0;
            for (int j = 0; j < grid.Nz; j++)
                for (int i = 0; i < grid.Nx; i++)
                {
                    int q = j * grid.Nx + i;
                    if (grid.Mask[q] == 0)
                        continue;
                    inside++;
                    float o = SkiSlopeClassifier.CellValue(grid.Heights, grid.Mask, grid.Nx, grid.Nz, i, j, in cp, out bool full,
                        out float _, out float _, out float _);
                    if (BitConverter.SingleToInt32Bits(o) != BitConverter.SingleToInt32Bits(res.CellGrade[q]))
                        refactor++;
                    if (!full)
                        continue;
                    fullCells++;
                    if (!tiles.TryCell(grid.I0 + i, grid.J0 + j, out byte band, out float tg))
                    {
                        noTile++;
                        continue;
                    }
                    if (BitConverter.SingleToInt32Bits(tg) != BitConverter.SingleToInt32Bits(res.CellGrade[q]) || band != res.CellBand[q])
                        mismatch++;
                }
            Check(refactor == 0, w, refactor + " of " + inside + " inside cells: CellValue on the ring grid != ClassifyRing's CellGrade");
            Check(noTile == 0, w, noTile + " full-window cells have no tile");
            Check(mismatch == 0, w, mismatch + " of " + fullCells + " full-window cells: tile value != ring CellGrade (bit-exact)");
            Check(fullCells > inside / 2, w, "only " + fullCells + " of " + inside + " cells had a full window");
            Check(t.OutOfRange == 0, w, t.OutOfRange + " samples outside the test terrain");
            Console.WriteLine("INFO  overlay parity " + name + ": " + fullCells + " full-window cells of " + inside + " inside cells bit-identical to the ring's " +
                              "CellGrade; band shares " + Shares(tiles, rcx, rcz, 300f));
        }

        private static string Shares(SkiSlopeOverlayTiles tiles, double cx, double cz, float radius)
        {
            int[] c = new int[5];
            int n = 0;
            int i0 = tiles.NodeOf(cx - radius, tiles.Origin.x), i1 = tiles.NodeOf(cx + radius, tiles.Origin.x);
            int j0 = tiles.NodeOf(cz - radius, tiles.Origin.y), j1 = tiles.NodeOf(cz + radius, tiles.Origin.y);
            for (int j = j0; j <= j1; j++)
                for (int i = i0; i <= i1; i++)
                    if (tiles.TryCell(i, j, out byte b, out float _) && b <= 4)
                    {
                        c[b]++;
                        n++;
                    }
            return string.Format("flat {0:P0} green {1:P0} blue {2:P0} black {3:P0} double {4:P0}", c[0] / (double)n, c[1] / (double)n,
                c[2] / (double)n, c[3] / (double)n, c[4] / (double)n);
        }

        private static void OverlayFade()
        {
            string w = "overlay/fade";
            Check(SkiSlopeOverlayTiles.FadeLevel(-1.0, 15f) == 1, w, "outside edge");
            Check(SkiSlopeOverlayTiles.FadeLevel(0.0, 15f) == 1, w, "at the edge");
            Check(SkiSlopeOverlayTiles.FadeLevel(4.0, 15f) == 2, w, "4 m in");
            Check(SkiSlopeOverlayTiles.FadeLevel(14.9, 15f) == SkiSlopeOverlayTiles.FadeLevels, w, "14.9 m in");
            Check(SkiSlopeOverlayTiles.FadeLevel(15.0, 15f) == SkiSlopeOverlayTiles.FadeLevels, w, "15 m in");
            Check(SkiSlopeOverlayTiles.FadeLevel(3.0, 0f) == SkiSlopeOverlayTiles.FadeLevels, w, "no fade");
            Check(SkiSlopeOverlayTiles.FadeLevel(double.NaN, 15f) == 1, w, "NaN");
            // DisplayUnits.StripKey reads the green band as raw 1 (ResortBatch links DisplayUnits without the classifier).
            string k = "overlay/stripkey";
            Check((byte)SlopeBand.Green == 1, k, "SlopeBand.Green moved off 1: DisplayUnits.StripKey and EuClassOf read it raw");
            Check(DisplayUnits.StripKey((byte)SlopeBand.Green, 0.20f, true) == DisplayUnits.StripKeyEuBlue, k, "EU 20 % green cell");
            Check(DisplayUnits.StripKey((byte)SlopeBand.Green, 0.10f, true) == (byte)SlopeBand.Green, k, "EU 10 % green cell");
            Check(DisplayUnits.StripKey((byte)SlopeBand.Green, 0.20f, false) == (byte)SlopeBand.Green, k, "NA 20 % green cell");
            Check(DisplayUnits.StripKey((byte)SlopeBand.Blue, 0.30f, true) == (byte)SlopeBand.Blue, k, "EU blue-band cell");
        }

        private static void OverlayRuns()
        {
            string w = "overlay/runs";
            const double cx = 300.7, cz = 211.3;
            const float r = 300f;
            Terrain t = OverlayTerrain(OverlaySteep(), cx, cz, r);
            var ts = new TerrainSampler { T = t };
            var tiles = new SkiSlopeOverlayTiles();
            tiles.SetLattice(Terrain.Origin, Terrain.Texel);
            tiles.Update(ref ts, cx, cz, r, 1, 1e9f);
            var runs = new List<SlopeOverlayRun>();
            var none = new NoSlopeCellSkip();
            bool ok = tiles.BuildRuns(cx, cz, r, 15f, false, 1, 100000, ref none, runs);
            Check(ok, w, "cap 100000 reported overflow");

            // Every disk cell exactly once; runs never touch with the same key and fade (maximal merge).
            var seen = new HashSet<long>();
            int dup = 0, notMax = 0, outside = 0, wrongKey = 0;
            for (int k = 0; k < runs.Count; k++)
            {
                SlopeOverlayRun ru = runs[k];
                for (int i = ru.I0; i <= ru.I1; i++)
                {
                    if (!seen.Add(SkiSlopeOverlayTiles.Key(i, ru.J)))
                        dup++;
                    double dx = Terrain.Origin.x + i * (double)Terrain.Texel - cx, dz = Terrain.Origin.y + ru.J * (double)Terrain.Texel - cz;
                    if (dx * dx + dz * dz > (double)r * r + 1e-6)
                        outside++;
                    tiles.TryCell(i, ru.J, out byte b, out float g);
                    if (DisplayUnits.StripKey(b, g, false) != ru.Key)
                        wrongKey++;
                }
                if (k > 0 && runs[k - 1].J == ru.J && runs[k - 1].I1 + 1 == ru.I0 && runs[k - 1].Key == ru.Key && runs[k - 1].Fade == ru.Fade)
                    notMax++;
            }
            int diskCells = 0;
            for (int j = tiles.NodeOf(cz - r, Terrain.Origin.y) - 1; j <= tiles.NodeOf(cz + r, Terrain.Origin.y) + 1; j++)
                for (int i = tiles.NodeOf(cx - r, Terrain.Origin.x) - 1; i <= tiles.NodeOf(cx + r, Terrain.Origin.x) + 1; i++)
                {
                    double dx = Terrain.Origin.x + i * (double)Terrain.Texel - cx, dz = Terrain.Origin.y + j * (double)Terrain.Texel - cz;
                    if (dx * dx + dz * dz <= (double)r * r)
                        diskCells++;
                }
            Check(dup == 0, w, dup + " cells in two runs");
            Check(outside == 0, w, outside + " cells outside the disk");
            Check(wrongKey == 0, w, wrongKey + " cells with the wrong key");
            Check(notMax == 0, w, notMax + " adjacent runs with the same key and fade");
            Check(seen.Count == diskCells, w, seen.Count + " cells in runs vs " + diskCells + " disk cells");

            // European key: a green cell at 15 % or more is key 5.
            var runsEu = new List<SlopeOverlayRun>();
            tiles.BuildRuns(cx, cz, r, 15f, true, 1, 100000, ref none, runsEu);
            int eu5 = 0;
            foreach (SlopeOverlayRun ru in runsEu)
                if (ru.Key == DisplayUnits.StripKeyEuBlue)
                    eu5++;
            Check(runsEu.Count >= runs.Count, w, "European runs " + runsEu.Count + " < " + runs.Count);
            Console.WriteLine("INFO  overlay runs: 300 m disk " + diskCells + " cells, " + runs.Count + " runs (stride 1), European " + runsEu.Count +
                              " (" + eu5 + " European-blue runs); " + Shares(tiles, cx, cz, r));

            // Stride 2: about half the rows, the same cells per drawn row.
            var runs2 = new List<SlopeOverlayRun>();
            tiles.BuildRuns(cx, cz, r, 15f, false, 2, 100000, ref none, runs2);
            var rows1 = new HashSet<int>();
            var rows2 = new HashSet<int>();
            foreach (SlopeOverlayRun ru in runs)
                rows1.Add(ru.J);
            foreach (SlopeOverlayRun ru in runs2)
                rows2.Add(ru.J);
            Check(rows2.Count * 2 >= rows1.Count - 1 && rows2.Count * 2 <= rows1.Count + 2, w, "stride 2 rows " + rows2.Count + " vs " + rows1.Count);

            // Cap: false and exactly cap runs.
            var capped = new List<SlopeOverlayRun>();
            bool okCap = tiles.BuildRuns(cx, cz, r, 15f, false, 1, 50, ref none, capped);
            Check(!okCap && capped.Count == 50, w, "cap 50: returned " + okCap + " with " + capped.Count + " runs");

            // Skip box: no run cell inside it.
            int ci = tiles.NodeOf(cx, Terrain.Origin.x), cj = tiles.NodeOf(cz, Terrain.Origin.y);
            var skip = new SkipBox { I0 = ci - 10, I1 = ci + 10, J0 = cj - 5, J1 = cj + 5 };
            var runsS = new List<SlopeOverlayRun>();
            tiles.BuildRuns(cx, cz, r, 15f, false, 1, 100000, ref skip, runsS);
            int inBox = 0, cellsS = 0;
            foreach (SlopeOverlayRun ru in runsS)
                for (int i = ru.I0; i <= ru.I1; i++)
                {
                    cellsS++;
                    if (i >= skip.I0 && i <= skip.I1 && ru.J >= skip.J0 && ru.J <= skip.J1)
                        inBox++;
                }
            Check(inBox == 0 && cellsS == diskCells - 21 * 11, w, "skip box: " + inBox + " cells inside, " + cellsS + " cells vs " + (diskCells - 21 * 11));
            Check(skip.Asked > 0, w, "skip never asked");

            // A missing tile leaves a gap, never a wrong color: an empty cache gives no runs.
            var empty = new SkiSlopeOverlayTiles();
            empty.SetLattice(Terrain.Origin, Terrain.Texel);
            var runsE = new List<SlopeOverlayRun>();
            empty.BuildRuns(cx, cz, r, 15f, false, 1, 100000, ref none, runsE);
            Check(runsE.Count == 0, w, "empty cache gave " + runsE.Count + " runs");
        }

        private static void OverlayVerify()
        {
            string w = "overlay/verify";
            const double cx = -510.2, cz = 640.9;
            const float r = 150f;
            Terrain t = OverlayTerrain(OverlaySteep(), cx, cz, r);
            var ts = new TerrainSampler { T = t };
            var tiles = new SkiSlopeOverlayTiles();
            tiles.SetLattice(Terrain.Origin, Terrain.Texel);
            tiles.Update(ref ts, cx, cz, r, 7, 1e9f);
            int count = tiles.Count;

            // A clean pass invalidates nothing.
            int bad = tiles.Verify(ref ts, 7, 1 << 22);
            Check(bad == 0, w, "clean terrain invalidated " + bad + " tiles");
            Check(tiles.NodesVerified > 0, w, "verified no node");

            // Raise one texel near the center: the tiles whose padded heights hold it go invalid, the others stay.
            int k = tiles.NodeOf(cx, Terrain.Origin.x), l = tiles.NodeOf(cz, Terrain.Origin.y);
            t.Add(k, l, 2.5f);
            int expect = 0;
            int pad = tiles.Pad, n = SkiSlopeOverlayTiles.TileCells;
            for (int tj = SkiSlopeOverlayTiles.FloorDiv(l - 64, n); tj <= SkiSlopeOverlayTiles.FloorDiv(l + 64, n); tj++)
                for (int ti = SkiSlopeOverlayTiles.FloorDiv(k - 64, n); ti <= SkiSlopeOverlayTiles.FloorDiv(k + 64, n); ti++)
                    if (tiles.Find(ti, tj) != null && k >= ti * n - pad && k <= ti * n + n - 1 + pad && l >= tj * n - pad && l <= tj * n + n - 1 + pad)
                        expect++;
            int got = 0;
            for (int pass = 0; pass < 4 && got < expect; pass++)
                got += tiles.Verify(ref ts, 7, 1 << 22);
            Check(got == expect && expect >= 1, w, "invalidated " + got + " tiles, expected " + expect);

            // Recompute = a fresh cache on the changed terrain.
            tiles.Update(ref ts, cx, cz, r, 7, 1e9f);
            var fresh = new SkiSlopeOverlayTiles();
            fresh.SetLattice(Terrain.Origin, Terrain.Texel);
            fresh.Update(ref ts, cx, cz, r, 7, 1e9f);
            int diff = 0, cells = 0;
            for (int j = l - 60; j <= l + 60; j++)
                for (int i = k - 60; i <= k + 60; i++)
                {
                    bool a = tiles.TryCell(i, j, out byte ba, out float ga), b = fresh.TryCell(i, j, out byte bb, out float gb);
                    if (!a && !b)
                        continue;
                    cells++;
                    if (a != b || ba != bb || BitConverter.SingleToInt32Bits(ga) != BitConverter.SingleToInt32Bits(gb))
                        diff++;
                }
            Check(diff == 0 && cells > 0, w, diff + " of " + cells + " cells differ from a fresh cache after the recompute");
            Check(tiles.Count == count, w, "tile count " + tiles.Count + " vs " + count);

            // Budget: one tile per call at least, the rest reported missing.
            var slow = new SkiSlopeOverlayTiles();
            slow.SetLattice(Terrain.Origin, Terrain.Texel);
            int miss = slow.Update(ref ts, cx, cz, r, 1, 0f);
            Check(slow.TilesComputed == 1 && miss == count - 1, w, "budget 0: computed " + slow.TilesComputed + ", missing " + miss + " of " + count);

            // A lattice change drops every tile.
            tiles.SetLattice(Terrain.Origin + new double2(1.0, 0.0), Terrain.Texel);
            Check(tiles.Count == 0, w, "lattice change kept " + tiles.Count + " tiles");
            Check(!tiles.SetLattice(Terrain.Origin, 0f) && !tiles.SetLattice(new double2(double.NaN, 0), Terrain.Texel), w, "bad lattice accepted");
        }

        private static void OverlayEvict()
        {
            string w = "overlay/evict";
            const float r = 150f;
            Terrain t = Terrain.Build(OverlaySteep(), -900, -400, 900, 400);
            var ts = new TerrainSampler { T = t };
            var tiles = new SkiSlopeOverlayTiles { Capacity = 20 };
            tiles.SetLattice(Terrain.Origin, Terrain.Texel);
            int frame = 0;
            for (double x = -600; x <= 600; x += 50)
            {
                frame++;
                tiles.Update(ref ts, x, 0, r, frame, 1e9f);
                int need = 0, have = 0;
                for (int j = tiles.NodeOf(-r, Terrain.Origin.y); j <= tiles.NodeOf(r, Terrain.Origin.y); j += 8)
                    for (int i = tiles.NodeOf(x - r, Terrain.Origin.x); i <= tiles.NodeOf(x + r, Terrain.Origin.x); i += 8)
                    {
                        double dx = Terrain.Origin.x + i * (double)Terrain.Texel - x, dz = Terrain.Origin.y + j * (double)Terrain.Texel;
                        if (dx * dx + dz * dz > (double)r * r)
                            continue;
                        need++;
                        if (tiles.TryCell(i, j, out byte _, out float _))
                            have++;
                    }
                if (!Check(have == need, w, "x " + x + ": " + have + " of " + need + " disk cells cached"))
                    break;
                if (!Check(tiles.Count <= Math.Max(20, CountDiskTiles(tiles, x, 0, r)), w, "x " + x + ": " + tiles.Count + " tiles above the cap"))
                    break;
            }
            Check(tiles.TilesEvicted > 0, w, "nothing evicted along a 1.2 km sweep with capacity 20");
        }

        private static int CountDiskTiles(SkiSlopeOverlayTiles tiles, double cx, double cz, float r)
        {
            var set = new HashSet<long>();
            for (int j = tiles.NodeOf(cz - r, tiles.Origin.y); j <= tiles.NodeOf(cz + r, tiles.Origin.y); j++)
                for (int i = tiles.NodeOf(cx - r, tiles.Origin.x); i <= tiles.NodeOf(cx + r, tiles.Origin.x); i++)
                    set.Add(SkiSlopeOverlayTiles.Key(SkiSlopeOverlayTiles.FloorDiv(i, 32), SkiSlopeOverlayTiles.FloorDiv(j, 32)));
            return set.Count;
        }

        private static void OverlayTiming()
        {
            const double cx = 40.0, cz = 75.0;
            foreach (float r in new[] { 150f, 300f })
            {
                Terrain t = OverlayTerrain(OverlaySteep(), cx, cz, r);
                var ts = new TerrainSampler { T = t };
                var tiles = new SkiSlopeOverlayTiles();
                tiles.SetLattice(Terrain.Origin, Terrain.Texel);
                tiles.Update(ref ts, cx + 2000, cz, r, 1, 1e9f);   // warm-up (JIT) on another spot
                tiles.Clear();
                var sw = Stopwatch.StartNew();
                tiles.Update(ref ts, cx, cz, r, 2, 1e9f);
                sw.Stop();
                double cold = sw.Elapsed.TotalMilliseconds;
                int n = tiles.Count;
                var runs = new List<SlopeOverlayRun>();
                var none = new NoSlopeCellSkip();
                tiles.BuildRuns(cx, cz, r, 15f, false, 1, 100000, ref none, runs);
                sw.Restart();
                const int reps = 50;
                for (int k = 0; k < reps; k++)
                    tiles.BuildRuns(cx + 0.3 * k, cz, r, 15f, false, 1, 100000, ref none, runs);
                sw.Stop();
                double build = sw.Elapsed.TotalMilliseconds / reps;
                sw.Restart();
                int verified = 0;
                for (int k = 0; k < reps; k++)
                {
                    long before = tiles.NodesVerified;
                    tiles.Verify(ref ts, 2, 2048);
                    verified += (int)(tiles.NodesVerified - before);
                }
                sw.Stop();
                Console.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "TIME  overlay radius {0} m: cold disk {1} tiles in {2:F2} ms ({3:F3} ms/tile), BuildRuns {4} runs {5:F3} ms, Verify 2048 nodes {6:F3} ms " +
                    "(CoreCLR; Mono about 5-8x)", r, n, cold, cold / Math.Max(1, n), runs.Count, build, sw.Elapsed.TotalMilliseconds / reps));
            }
        }
    }
}
