// Host mode rf-realize, the "seen" report (post-release round 1, T171 / T172 / T176): the realized plan as a player sees it on the
// map, not as the game's link rule reads it. Three counts per region, printed as one SEEN line and written to seen.tsv:
//   - stations: pairs of stations of two lifts whose lot discs overlap (PlanGeometry.StationHalfDiagonalOf), and the smallest gap;
//   - ends: how far each piste end and each track end stops from what it meets (another outline, a lift station, the bottom band).
//     The game links two outlines within 25 m (LiftServedRule.LinkMeters), so a 20 m hole in the snow is "linked" and still looks cut;
//   - tracks: the areas each track chain became, and the joints inside a chain (one traverse built as several cat tracks).
// RZ_RENDER=1 also writes <out dir>/<region>-seen.png: the outlines at 2 m per pixel (pistes by the band the game shows, the tracks in a
// color per chain, stations as their lot discs, every end over SeenGapM circled red). RZ_VIEW="x,z,half" renders only the square of
// that half size (m) around world (x, z), at 0.5 m per pixel. Report only: no checks.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using SkiResortMod.Systems;
using Unity.Mathematics;

namespace AutoGenBatch
{
    internal static partial class RealizerHost
    {
        private const float SeenTouchM = RunsFirstRealizer.MergeGapM + 1f, SeenGapM = 10f, SeenLinkM = 25f;   // an end within 3 m touches; over 10 m reads as a hole
        private const float SeenEndReachM = 45f;      // the ring nodes this near a centerline end are that end's nodes (a closed cap moves up to 40 m)
        private const float SeenShortTrackM = 50f;    // a track area under this length reads as a scrap
        private const int SpurSegment = RunsFirstRealizer.SpurSegment;

        internal sealed class Seen
        {
            public int Lifts, StationClashes;
            public float StationMinGapM = float.PositiveInfinity;
            public readonly List<string> Clashes = new List<string>();
            // [0] touch, [1] to SeenGapM, [2] to the link reach, [3] free (over 25 m from everything)
            public readonly int[] PisteTop = new int[4], PisteBottom = new int[4], TrackEnd = new int[4];
            public int PisteBase;   // piste bottoms in the bottom band (nothing to meet)
            public readonly List<(float2 P, float Gap, string What)> Holes = new List<(float2, float, string)>();
            public int TrackChains, TrackAreas, ChainsInPieces, MaxPieces, TrackJoints, ShortTracks;
            public double TrackKm, JointM;

            public int PisteHoles => PisteTop[2] + PisteTop[3] + PisteBottom[2] + PisteBottom[3];
            public int TrackHoles => TrackEnd[2] + TrackEnd[3];
        }

        /// <summary>The distance from one end of an area to the nearest thing it can meet: another outline (0 when the end lies inside
        /// it), or a lift station's lot disc (tops for a start, bottoms for an end, both for a track).</summary>
        private static float EndGap(AutoPlanResult res, int self, bool start, out string what)
        {
            AutoRun r = res.Runs[self];
            float2 e = start ? r.Centerline[0] : r.Centerline[r.Centerline.Length - 1];
            var mine = new List<float2> { e };
            foreach (float2 v in r.Ring)
                if (math.distance(v, e) <= SeenEndReachM)
                    mine.Add(v);
            float best = float.PositiveInfinity;
            what = "nothing";
            for (int j = 0; j < res.Runs.Count; j++)
            {
                if (j == self)
                    continue;
                AutoRun o = res.Runs[j];
                float d = float.PositiveInfinity;
                foreach (float2 v in mine)
                {
                    d = Math.Min(d, GradeRaster.Inside(o.Ring, v) ? 0f : PlanGeometry.PointRingDistance(o.Ring, v));
                    if (d <= 0f)
                        break;
                }
                if (d < best)
                {
                    best = d;
                    what = (o.Kind == RunKind.CatTrack ? "track " : "piste ") + o.Source.ToString(CultureInfo.InvariantCulture);
                }
            }
            bool track = r.Kind == RunKind.CatTrack;
            PlanLift[] lifts = res.Resort?.Lifts ?? new PlanLift[0];
            for (int l = 0; l < lifts.Length; l++)
                for (int s = 0; s < 2; s++)
                {
                    if (!track && (s == 0) == start)
                        continue;   // a piste starts at a top and ends at a bottom
                    float2 st = s == 0 ? lifts[l].Bottom.xz : lifts[l].Top.xz;
                    float d = Math.Max(0f, math.distance(e, st) - PlanGeometry.StationHalfDiagonalOf(lifts[l].TypeIndex));
                    if (d < best)
                    {
                        best = d;
                        what = $"L{l + 1} {(s == 0 ? "bottom" : "top")}";
                    }
                }
            return best;
        }

        private static int GapBin(float g) => g <= SeenTouchM ? 0 : g <= SeenGapM ? 1 : g <= SeenLinkM ? 2 : 3;

        internal static Seen SeenOf(AutoPlanResult res)
        {
            var m = new Seen();
            PlanLift[] lifts = res.Resort?.Lifts ?? new PlanLift[0];
            m.Lifts = lifts.Length;
            for (int i = 0; i < lifts.Length; i++)
                for (int k = i + 1; k < lifts.Length; k++)
                    for (int s = 0; s < 2; s++)
                        for (int q = 0; q < 2; q++)
                        {
                            float2 a = s == 0 ? lifts[i].Bottom.xz : lifts[i].Top.xz, b = q == 0 ? lifts[k].Bottom.xz : lifts[k].Top.xz;
                            float gap = math.distance(a, b) - PlanGeometry.StationHalfDiagonalOf(lifts[i].TypeIndex) - PlanGeometry.StationHalfDiagonalOf(lifts[k].TypeIndex);
                            m.StationMinGapM = Math.Min(m.StationMinGapM, gap);
                            if (gap >= 0f)
                                continue;
                            m.StationClashes++;
                            m.Clashes.Add($"L{i + 1} {(s == 0 ? "bottom" : "top")} / L{k + 1} {(q == 0 ? "bottom" : "top")} {math.distance(a, b):F0} m apart");
                        }
            var chains = new Dictionary<int, List<AutoRun>>();
            for (int i = 0; i < res.Runs.Count; i++)
            {
                AutoRun r = res.Runs[i];
                bool track = r.Kind == RunKind.CatTrack;
                for (int s = 0; s < 2; s++)
                {
                    bool start = s == 0;
                    if (!track && !start && r.TouchesBase)
                    {
                        m.PisteBase++;
                        continue;
                    }
                    float g = EndGap(res, i, start, out string what);
                    (track ? m.TrackEnd : start ? m.PisteTop : m.PisteBottom)[GapBin(g)]++;
                    if (g > SeenGapM)
                        m.Holes.Add((start ? r.Centerline[0] : r.Centerline[r.Centerline.Length - 1], g,
                            $"{(track ? "track" : "piste")} {r.Source} segment {r.Segment} {(start ? "start" : "end")}: {g:F0} m from {what}"));
                }
                if (!track)
                    continue;
                m.TrackAreas++;
                m.TrackKm += r.LengthM / 1000.0;
                if (r.LengthM < SeenShortTrackM)
                    m.ShortTracks++;
                if (!chains.TryGetValue(r.Source, out List<AutoRun> list))
                    chains[r.Source] = list = new List<AutoRun>();
                list.Add(r);
            }
            m.TrackChains = chains.Count;
            foreach (List<AutoRun> list in chains.Values)
            {
                m.MaxPieces = Math.Max(m.MaxPieces, list.Count);
                if (list.Count > 1)
                    m.ChainsInPieces++;
                list.Sort((a, b) => a.Segment.CompareTo(b.Segment));
                for (int k = 1; k < list.Count; k++)
                {
                    if (list[k].Segment >= SpurSegment)
                        break;
                    m.TrackJoints++;
                    m.JointM += math.distance(list[k - 1].Centerline[list[k - 1].Centerline.Length - 1], list[k].Centerline[0]);
                }
            }
            return m;
        }

        private static string Bins(int[] b) => $"{b[0]}/{b[1]}/{b[2]}/{b[3]}";

        internal static string SeenLine(string id, Seen m) =>
            $"SEEN {id}: stations {m.StationClashes} lot clash(es) of {m.Lifts} lifts (smallest gap {(m.Lifts > 1 ? m.StationMinGapM.ToString("F0", CultureInfo.InvariantCulture) : "-")} m); " +
            $"ends touch/to {SeenGapM:F0} m/to {SeenLinkM:F0} m/free: piste tops {Bins(m.PisteTop)}, piste bottoms {Bins(m.PisteBottom)} (+{m.PisteBase} in the bottom band), " +
            $"track ends {Bins(m.TrackEnd)}; holes over {SeenGapM:F0} m: pistes {m.PisteHoles}, tracks {m.TrackHoles}; " +
            $"tracks: {m.TrackChains} chains as {m.TrackAreas} areas ({m.TrackKm:F2} km), {m.ChainsInPieces} in pieces (most {m.MaxPieces}), " +
            $"{m.TrackJoints} joints inside a chain ({m.JointM:F0} m), {m.ShortTracks} under {SeenShortTrackM:F0} m";

        private static void SeenReport(string id, AutoPlanResult res, HeightSnapshot snap, float2[] region, string outDir, List<string> lines)
        {
            Seen m = SeenOf(res);
            string line = SeenLine(id, m);
            Console.WriteLine(line);
            lines.Add(line);
            foreach (string c in m.Clashes)
                lines.Add("  seen: lots overlap: " + c);
            foreach (var (p, _, what) in m.Holes)
                lines.Add($"  seen: hole at ({p.x:F0}, {p.y:F0}): {what}");
            string tsv = Path.Combine(outDir, "seen.tsv");
            if (!File.Exists(tsv))
                File.WriteAllText(tsv, "region\tlifts\tstationClashes\tstationMinGap\tpisteTops\tpisteBottoms\tpisteBase\ttrackEnds\tpisteHoles\ttrackHoles\t" +
                    "trackChains\ttrackAreas\ttrackKm\tchainsInPieces\tmaxPieces\ttrackJoints\tjointM\tshortTracks\n");
            File.AppendAllText(tsv, string.Join("\t", new object[]
            {
                id, m.Lifts, m.StationClashes, m.Lifts > 1 ? m.StationMinGapM.ToString("F0", CultureInfo.InvariantCulture) : "-", Bins(m.PisteTop), Bins(m.PisteBottom),
                m.PisteBase, Bins(m.TrackEnd), m.PisteHoles, m.TrackHoles, m.TrackChains, m.TrackAreas, m.TrackKm.ToString("F2", CultureInfo.InvariantCulture),
                m.ChainsInPieces, m.MaxPieces, m.TrackJoints, m.JointM.ToString("F0", CultureInfo.InvariantCulture), m.ShortTracks,
            }) + "\n");
            if (Environment.GetEnvironmentVariable("RZ_RENDER") == "1")
                SeenRender(Path.Combine(outDir, id + "-seen.png"), res, m, snap, region);
        }

        // ---------------------------------------------------------------- the render (RZ_RENDER=1)


        private static void SeenRender(string path, AutoPlanResult res, Seen m, HeightSnapshot snap, float2[] region)
        {
            float2 lo = new float2(float.MaxValue), hi = new float2(float.MinValue);
            foreach (float2 p in region)
            {
                lo = math.min(lo, p);
                hi = math.max(hi, p);
            }
            lo -= 40f;
            hi += 40f;
            float RenderM = 2f;   // meters per pixel
            string[] view = (Environment.GetEnvironmentVariable("RZ_VIEW") ?? "").Split(',');
            if (view.Length == 3 && float.TryParse(view[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float vx) &&
                float.TryParse(view[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float vz) && float.TryParse(view[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float vh))
            {
                lo = new float2(vx - vh, vz - vh);
                hi = new float2(vx + vh, vz + vh);
                RenderM = 0.5f;
            }
            int w = (int)Math.Ceiling((hi.x - lo.x) / RenderM), h = (int)Math.Ceiling((hi.y - lo.y) / RenderM);
            var rgb = new byte[w * h * 3];
            float2 Px(float2 p) => new float2((p.x - lo.x) / RenderM, (hi.y - p.y) / RenderM);   // north up
            void Set(int x, int y, int r, int g, int b, float a)
            {
                if (x < 0 || y < 0 || x >= w || y >= h)
                    return;
                int o = (y * w + x) * 3;
                rgb[o] = (byte)(rgb[o] + (r - rgb[o]) * a);
                rgb[o + 1] = (byte)(rgb[o + 1] + (g - rgb[o + 1]) * a);
                rgb[o + 2] = (byte)(rgb[o + 2] + (b - rgb[o + 2]) * a);
            }
            // the ground: a hillshade of the snapshot, darker outside the region
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float2 p = new float2(lo.x + (x + 0.5f) * RenderM, hi.y - (y + 0.5f) * RenderM);
                    float c = snap.Sample(new float3(p.x, 0f, p.y));
                    float ex = snap.Sample(new float3(p.x + 8f, 0f, p.y)), ez = snap.Sample(new float3(p.x, 0f, p.y + 8f));
                    float shade = float.IsNaN(c) || float.IsNaN(ex) || float.IsNaN(ez) ? 0.25f : math.saturate(0.55f + 0.9f * ((c - ex) + (ez - c)) / 16f);
                    int v = (int)(60 + 120 * shade);
                    Set(x, y, v, v, v + 6, 1f);
                }
            void Line(float2 a, float2 b, int r, int g, int bl, float a2 = 1f)
            {
                float2 pa = Px(a), pb = Px(b);
                int n = Math.Max(1, (int)Math.Ceiling(math.distance(pa, pb) * 2f));
                for (int k = 0; k <= n; k++)
                {
                    float2 q = math.lerp(pa, pb, k / (float)n);
                    Set((int)q.x, (int)q.y, r, g, bl, a2);
                }
            }
            void Fill(float2[] ring, int r, int g, int bl, float a)
            {
                float2 rl = new float2(float.MaxValue), rh = new float2(float.MinValue);
                foreach (float2 p in ring)
                {
                    float2 q = Px(p);
                    rl = math.min(rl, q);
                    rh = math.max(rh, q);
                }
                for (int y = Math.Max(0, (int)rl.y); y <= Math.Min(h - 1, (int)rh.y + 1); y++)
                    for (int x = Math.Max(0, (int)rl.x); x <= Math.Min(w - 1, (int)rh.x + 1); x++)
                        if (GradeRaster.Inside(ring, new float2(lo.x + (x + 0.5f) * RenderM, hi.y - (y + 0.5f) * RenderM)))
                            Set(x, y, r, g, bl, a);
            }
            void Circle(float2 c, float radM, int r, int g, int bl)
            {
                int n = Math.Max(24, (int)(radM * 4f));
                for (int k = 0; k < n; k++)
                {
                    float a0 = k * 2f * (float)Math.PI / n, a1 = (k + 1) * 2f * (float)Math.PI / n;
                    Line(c + radM * new float2((float)Math.Cos(a0), (float)Math.Sin(a0)), c + radM * new float2((float)Math.Cos(a1), (float)Math.Sin(a1)), r, g, bl);
                }
            }
            for (int i = 0, j = region.Length - 1; i < region.Length; j = i++)
                Line(region[j], region[i], 255, 255, 255, 0.5f);
            (int, int, int) Band(SlopeBand b) => b <= SlopeBand.Green ? (60, 190, 80) : b == SlopeBand.Blue ? (60, 120, 255) : b == SlopeBand.Black ? (30, 30, 30) : (120, 0, 0);
            foreach (AutoRun r in res.Runs)
            {
                bool track = r.Kind == RunKind.CatTrack;
                // a track chain keeps one color over its pieces (six pale hues by its source)
                int hue = Math.Abs(r.Source) % 6;
                var (cr, cg, cb) = !track ? Band(r.Band) : hue == 0 ? (190, 255, 150) : hue == 1 ? (255, 240, 140) : hue == 2 ? (255, 190, 230) : hue == 3 ? (160, 240, 255) : hue == 4 ? (255, 200, 150) : (220, 200, 255);
                Fill(r.Ring, cr, cg, cb, track ? 0.85f : 0.7f);
            }
            foreach (AutoRun r in res.Runs)
            {
                bool track = r.Kind == RunKind.CatTrack;
                for (int i = 0, j = r.Ring.Length - 1; i < r.Ring.Length; j = i++)
                    Line(r.Ring[j], r.Ring[i], track ? 0 : 255, track ? 70 : 255, track ? 0 : 255);
            }
            PlanLift[] lifts = res.Resort?.Lifts ?? new PlanLift[0];
            foreach (PlanLift l in lifts)
            {
                Line(l.Bottom.xz, l.Top.xz, 255, 220, 0);
                float rad = PlanGeometry.StationHalfDiagonalOf(l.TypeIndex);
                Circle(l.Bottom.xz, rad, 255, 220, 0);
                Circle(l.Top.xz, rad, 255, 140, 0);
                Circle(l.Top.xz, rad - 2f, 255, 140, 0);
            }
            foreach (var (p, gap, _) in m.Holes)
            {
                Circle(p, Math.Max(12f, gap), 255, 0, 0);
                Circle(p, Math.Max(12f, gap) + 2f, 255, 0, 0);
            }
            Program.WritePngRgb(path, w, h, rgb);
            File.WriteAllText(Path.ChangeExtension(path, ".map"), string.Format(CultureInfo.InvariantCulture,
                "{{\"x0\":{0},\"z1\":{1},\"mPerPx\":{2},\"w\":{3},\"h\":{4}}}\n", lo.x, hi.y, RenderM, w, h));
            Console.WriteLine($"SEEN render {Path.GetFileName(path)}: {w} x {h}, north up, 1 px = {RenderM:F0} m, world x = {lo.x:F0} + px x {RenderM:F0}, z = {hi.y:F0} - py x {RenderM:F0}");
        }
    }
}
