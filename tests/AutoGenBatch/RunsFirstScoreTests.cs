// Scorecard v7's ribbon band (RunsFirstScore.RibbonBand): a planner must not buy an easier band with a trick. The review of v7 found
// two: a kink or back-track that breaks the ribbon's mask (the classifier's P90 band) and a line that leaves the snapshot (not rated).
// Both now rate double black. Synthetic terrain, the review's probes: a 15 % slope with one 40 m pitch at 35 % across its full width
// (the straight line rates blue), and a 30 % slope with one 60 m band at 50 % (black).

using System;
using System.Collections.Generic;
using SkiResortMod.Systems;
using Unity.Mathematics;

namespace AutoGenBatch
{
    internal static partial class Program
    {
        private static void RunsFirstScoreTests()
        {
            const int Res = 4096;
            var scale = new float3(4096f / 14336f, 65535f / 4096f, 4096f / 14336f);
            var offset = new float3(7166.25f, 0f, 7166.25f);
            float2[] region = { new float2(-400, -50), new float2(400, -50), new float2(400, 1500), new float2(-400, 1500) };
            HeightSnapshot Snap(Func<float, float, float> hf)
            {
                if (!SkiRunAutoPlanner.SnapshotRect(region, Res, Res, scale, offset, out int x0, out int z0, out int w, out int d))
                    throw new InvalidOperationException("snapshot rect");
                var s = HeightSnapshot.Allocate(x0, z0, w, d, Res, Res, scale, offset, false);
                for (int l = 0; l < d; l++)
                    for (int k = 0; k < w; k++)
                    {
                        float x = (x0 + k) / scale.x - offset.x, z = (z0 + l) / scale.z - offset.z;
                        s.Heights[l * w + k] = (ushort)math.clamp(math.round(hf(x, z) * scale.y), 0, 65535);
                    }
                return s;
            }
            List<float2> Line(params (float x, float z)[] pts)   // densified to 10 m, as the planner's points
            {
                var o = new List<float2>();
                for (int i = 0; i + 1 < pts.Length; i++)
                {
                    float2 a = new float2(pts[i].x, pts[i].z), b = new float2(pts[i + 1].x, pts[i + 1].z);
                    int n = Math.Max(1, (int)Math.Ceiling(math.distance(a, b) / 10f));
                    for (int j = 0; j < n; j++)
                        o.Add(math.lerp(a, b, j / (float)n));
                }
                o.Add(new float2(pts[pts.Length - 1].x, pts[pts.Length - 1].z));
                return o;
            }
            var grid = new SlopeGrid();
            var res = new SlopeResult();
            SlopeBand Band(HeightSnapshot s, List<float2> l, out RunsFirstScore.RaterOutcome how) =>
                RunsFirstScore.RibbonBand(s, l, RunsFirstScore.RibbonHalfWidthM, grid, res, out how);

            const string where = "RunsFirstScore ribbon band";
            HeightSnapshot a = Snap((x, z) => 1000f + 0.15f * (1500f - z) + 0.20f * math.clamp(740f - z, 0f, 40f));
            SlopeBand straight = Band(a, Line((0, 20), (0, 1480)), out RunsFirstScore.RaterOutcome h0);
            Check(straight == SlopeBand.Blue && h0 == RunsFirstScore.RaterOutcome.Rated, where, $"the straight line over the pitch rates {straight} ({h0}), not Blue");
            var tricks = new (string Name, List<float2> Line)[]
            {
                ("an X-shaped kink at 400 m", Line((0, 20), (0, 400), (30, 370), (-30, 430), (-30, 1480))),
                ("a 60 m back-track", Line((0, 20), (0, 400), (0, 340), (5, 341), (5, 1480))),
                ("a back-track at the pitch", Line((0, 20), (0, 760), (0, 690), (3, 690), (3, 1480))),
                ("the end 60 m past the region's box", Line((0, 20), (0, 1560))),
            };
            var report = new List<string>();
            foreach ((string name, List<float2> l) in tricks)
            {
                SlopeBand b = Band(a, l, out RunsFirstScore.RaterOutcome how);
                Check(b >= straight, where, $"{name} rates {b} ({how}), softer than the straight line's {straight}");
                report.Add($"{name} {b} ({how})");
            }
            SlopeBand tiny = Band(a, Line((0, 100), (0, 100.3f)), out RunsFirstScore.RaterOutcome ht);
            Check(tiny == SlopeBand.DoubleBlack && ht == RunsFirstScore.RaterOutcome.Unrated, where, $"a 0.3 m line rates {tiny} ({ht}), not an unrated DoubleBlack");
            SlopeBand none = RunsFirstScore.RibbonBand(a, null, RunsFirstScore.RibbonHalfWidthM, grid, res, out RunsFirstScore.RaterOutcome hn);
            Check(none == SlopeBand.DoubleBlack && hn == RunsFirstScore.RaterOutcome.Unrated, where, $"a null centerline rates {none} ({hn})");

            // a given (built) outline keeps what the game shows: the X kink's ribbon as an outline is not connected and takes the P90 band
            float2[] xRing = RunsFirstScore.RibbonOf(tricks[0].Line, RunsFirstScore.RibbonHalfWidthM);
            SlopeBand given = RunsFirstScore.OutlineBand(a, xRing, grid, res, out RunsFirstScore.RaterOutcome hg);
            Check(hg == RunsFirstScore.RaterOutcome.NotConnected && given == (res.Band == SlopeBand.Flat ? SlopeBand.Green : res.Band), where,
                $"the kinked ribbon as a given outline: {given} ({hg}), not the classifier's P90 band of a mask that is not connected");

            HeightSnapshot c = Snap((x, z) => 1000f + 0.30f * (1500f - z) + 0.20f * math.clamp(760f - z, 0f, 60f));
            SlopeBand black = Band(c, Line((0, 20), (0, 1480)), out RunsFirstScore.RaterOutcome hc);
            SlopeBand past = Band(c, Line((0, 20), (0, 1560)), out RunsFirstScore.RaterOutcome hp);
            Check(black == SlopeBand.Black, where, $"the straight line over the 50 % band rates {black} ({hc}), not Black");
            Check(past >= black, where, $"the same line ending 60 m past the region's box rates {past} ({hp}), softer than {black}");
            Console.WriteLine($"REPORT rf score ribbon band: straight {straight}; {string.Join("; ", report)}; 0.3 m {tiny}; given kinked outline {given} ({hg}); " +
                $"terrain C straight {black}, past the box {past} ({hp})");
        }
    }
}
