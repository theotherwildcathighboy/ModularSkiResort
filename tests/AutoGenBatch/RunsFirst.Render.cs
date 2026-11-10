// RunsFirst host render: the host's part of RunsFirstPlanner (compiled by the test host only, never by the mod). RunRegion plans a
// region, replays v2 on it, scores the plan with the frozen scorecard (RunsFirst.Score.cs), writes the tables and renders the side by
// side PNG. The debug prints that only the host asks for (RF_V3DBG, RF_TURNDBG) are here too. Being a part of the planner class, it
// reads the planner's own types directly.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using AutoGenBatch;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public sealed partial class RunsFirstPlanner
    {
        /// <summary>The rf mode for one region: plan, v2 replay, tables, score, render (the debug and timing lines print as they come).</summary>
        internal void RunRegion(string id, float2[] ring, HeightmapFile file, string outDir)
        {
            TraceSink = Console.WriteLine;
            HeightSnapshot snap = Heightmap.Snapshot(file, ring);
            Planned pl = PlanCore(id, snap, ring);
            Grid g = pl.G;
            float[] acc = pl.Acc, accR = pl.AccR;
            List<Line> lines = pl.Lines;
            List<Lift> lifts = pl.Lifts;
            PlanDraft built = pl.Built;
            long ms = pl.Ms;
            // the per-pass time table (report only): each pass's own milliseconds, in plan order
            long prevMs = 0;
            Console.WriteLine($"REPORT rf time {id}: " + string.Join(", ", PassTimes.Select(t => { long d = t.Ms - prevMs; prevMs = t.Ms; return $"{t.Pass} {d}"; })) + $", total {ms} ms");
            Console.WriteLine($"REPORT rf hash {id}: {pl.Result.Hash():x16}, {pl.Result.Runs.Count} runs, {pl.Result.Lifts.Count} lifts, {pl.Result.Connectors.Count} connectors");

            // the v2 plan of the same region, as the host replays it
            AutoPlanKnobs knobs = AutoPlanKnobs.Default(SkiRunAutoPlanner.SeedOf(ring, 0));
            AutoPlanResult v2 = new SkiRunAutoPlanner().Run(new AutoPlanInput { Snapshot = snap, Region = ring, Knobs = knobs, Mode = AutoPlanMode.Resort });

            int picked = 0;
            var bands = new int[5];
            float km = 0f;
            foreach (Line l in lines)
                if (l.Picked)
                {
                    picked++;
                    bands[(int)l.Band]++;
                    km += l.Length / 1000f;
                }
            float km2 = (float)(SkiRunAutoPlanner.PolygonArea(ring) / 1e6);
            Console.WriteLine($"REPORT rf {id}: {km2:F2} km2, {lines.Count} candidate line(s), {picked} picked (G/B/K/DB {bands[1]}/{bands[2]}/{bands[3]}/{bands[4]}), " +
                $"{km:F1} km of runs, {ms} ms; v2 {v2.Status}: {v2.Runs.Count} run(s), {v2.Resort?.Lifts?.Length ?? 0} lift(s)");

            WriteTable(Path.Combine(outDir, id + ".txt"), id, lines);
            WriteLifts(Path.Combine(outDir, id + ".txt"), id, lifts, lines);

            // the scorecard (RunsFirst.Score.cs) reads the plan exactly as drawn (RunsFirst.Plan.cs)
            var plan = new ScorePlan();
            foreach (PlanRun r in built.Runs)
                if (r.Graded)
                    plan.Connectors.Add(r.Pts);
                else
                    plan.Runs.Add((r.Pts, r.Surface));
            plan.Connectors.AddRange(built.Connectors);
            foreach (Lift lf in lifts)
                plan.Lifts.Add((lf.Type, lf.Bottom.xz, lf.Top.xz));
            foreach (string s in built.Log)
                Console.WriteLine($"REPORT rf plan {id}: {s}");
            RunsFirstScore.Result score = RunsFirstScore.Evaluate(snap, ring, plan);
            Console.WriteLine(RunsFirstScore.Line(id, score));
            if (O.DebugV3)
                V3Debug(g, built, lifts);
            if (O.DebugTurns)
                TurnDebug(id, built);
            File.AppendAllText(Path.Combine(outDir, id + ".txt"), "\n" + RunsFirstScore.Line(id, score) + "\n" +
                string.Concat(score.Notes.Select(s => "  " + s + "\n")));
            Render(Path.Combine(outDir, id + ".png"), snap, ring, g, acc, accR, lines, v2, lifts, built);
        }

        // ------------------------------------------------------------------ grid and flow

        private static void WriteTable(string path, string id, List<Line> lines)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"# runs-first candidates {id}: map# id family picked band drop_m length_m mean rating steepest width_m score top(x,z,y) bottom(x,z,y) why");
            foreach (Line l in lines)
            {
                float3 t = l.Pts[0], b = l.Pts[l.Pts.Count - 1];
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{18} {0} {1}{19} {2} {3} {4:F0} {5:F0} {6:F2} {7:F2} {8:F2} {9:F0} {10:F0} ({11:F0},{12:F0},{13:F0}) ({14:F0},{15:F0},{16:F0}) {17}",
                    l.Id, l.Family, l.Picked ? "Y" : "-", l.Band, l.Drop, l.Length, l.Mean, l.Rating, l.Steepest, l.Width, l.Score,
                    t.x, t.z, t.y, b.x, b.z, b.y, l.Why, l.Picked ? "#" + l.Pick : "", l.Merge ? "+merge" : ""));
            }
            File.WriteAllText(path, sb.ToString());
        }

        // ------------------------------------------------------------------ render

        private sealed class Canvas
        {
            public int W, H;
            public byte[] Rgb;
            public Canvas(int w, int h)
            {
                W = w;
                H = h;
                Rgb = new byte[w * h * 3];
            }
            public void Set(int x, int y, (int r, int g, int b) c, float a = 1f)
            {
                if (x < 0 || y < 0 || x >= W || y >= H)
                    return;
                int o = (y * W + x) * 3;
                Rgb[o] = (byte)(Rgb[o] + (c.r - Rgb[o]) * a);
                Rgb[o + 1] = (byte)(Rgb[o + 1] + (c.g - Rgb[o + 1]) * a);
                Rgb[o + 2] = (byte)(Rgb[o + 2] + (c.b - Rgb[o + 2]) * a);
            }
            public void Disc(float x, float y, float r, (int, int, int) c)
            {
                for (int yy = (int)(y - r); yy <= (int)(y + r) + 1; yy++)
                    for (int xx = (int)(x - r); xx <= (int)(x + r) + 1; xx++)
                        if ((xx - x) * (xx - x) + (yy - y) * (yy - y) <= r * r)
                            Set(xx, yy, c);
            }
        }

        private static readonly string[] Digits =
        {
            "111101101101111", "010110010010111", "111001111100111", "111001111001111", "101101111001001",
            "111100111001111", "111100111101111", "111001001001001", "111101111101111", "111101111001111",
        };

        private static (int, int, int) BandColor(SlopeBand b) => b switch
        {
            SlopeBand.Green => (40, 170, 60),
            SlopeBand.Blue => (40, 110, 235),
            SlopeBand.Black => (10, 10, 10),
            SlopeBand.DoubleBlack => (10, 10, 10),
            _ => (200, 200, 200),
        };

        private static (int, int, int) EdgeColor(SlopeBand b) => b == SlopeBand.DoubleBlack ? (255, 150, 0) : (255, 255, 255);

        private static void Render(string path, HeightSnapshot s, float2[] ring, Grid g, float[] acc, float[] accR, List<Line> lines, AutoPlanResult v2, List<Lift> lifts, PlanDraft built)
        {
            int w = s.Width, d = s.Depth;
            float t = s.TexelM;
            Func<int, int, float> hAt = (i, j) => s.ToWorld(s.Heights[Clamp(j, 0, d - 1) * w + Clamp(i, 0, w - 1)]);
            // uphill = image top: the axis closest to lowest -> highest cell in the region
            int lo = -1, hi = -1;
            for (int c = 0; c < g.W * g.D; c++)
                if (g.In[c])
                {
                    if (lo < 0 || g.H[c] < g.H[lo])
                        lo = c;
                    if (hi < 0 || g.H[c] > g.H[hi])
                        hi = c;
                }
            float2 up = g.World(hi) - g.World(lo);
            int rot = Math.Abs(up.x) > Math.Abs(up.y) ? (up.x > 0 ? 1 : 3) : (up.y > 0 ? 0 : 2);   // 0 +z, 1 +x, 2 -z, 3 -x
            int iw = rot % 2 == 0 ? w : d, ih = rot % 2 == 0 ? d : w;
            Func<float, float, float2> img = (i, j) => rot switch
            {
                0 => new float2(i, d - 1 - j),
                1 => new float2(d - 1 - j, w - 1 - i),
                2 => new float2(w - 1 - i, j),
                _ => new float2(j, i),
            };
            Func<float2, float2> fromWorld = p => img((float)((p.x - s.Origin.x) / t - s.X0), (float)((p.y - s.Origin.y) / t - s.Z0));

            Canvas Base(bool flow)
            {
                var cv = new Canvas(iw, ih);
                for (int j = 0; j < d; j++)
                    for (int i = 0; i < w; i++)
                    {
                        float dzdx = (hAt(i + 1, j) - hAt(i - 1, j)) / (2 * t), dzdz = (hAt(i, j + 1) - hAt(i, j - 1)) / (2 * t);
                        float3 nrm = math.normalize(new float3(-dzdx, 1f, -dzdz));
                        float shade = math.max(0f, math.dot(nrm, math.normalize(new float3(-1f, 0.8f, 1f))));
                        float grade = math.sqrt(dzdx * dzdx + dzdz * dzdz);
                        float2 wp = new float2((float)(s.Origin.x + (s.X0 + i) * t), (float)(s.Origin.y + (s.Z0 + j) * t));
                        bool inside = Inside(ring, wp);
                        int v = (int)(60 + 170 * shade);
                        (int, int, int) col = (v, v, v);
                        if (grade > 0.70f)
                            col = (Math.Min(255, v + 40), (int)(v * 0.75f), (int)(v * 0.7f));   // cliff tint
                        else if (grade < 0.15f)
                            col = ((int)(v * 0.8f), Math.Min(255, v + 25), (int)(v * 0.8f));   // bench tint
                        // contours every 50 m, heavier every 250 m
                        float h0 = hAt(i, j);
                        int band0 = (int)Math.Floor(h0 / 50f);
                        if ((int)Math.Floor(hAt(i + 1, j) / 50f) != band0 || (int)Math.Floor(hAt(i, j + 1) / 50f) != band0)
                        {
                            bool major = (int)Math.Floor(Math.Max(Math.Max(hAt(i + 1, j), hAt(i, j + 1)), h0) / 250f) != (int)Math.Floor(Math.Min(Math.Min(hAt(i + 1, j), hAt(i, j + 1)), h0) / 250f);
                            float k = major ? 0.35f : 0.65f;
                            col = ((int)(col.Item1 * k), (int)(col.Item2 * k), (int)(col.Item3 * k));
                        }
                        if (!inside)
                        {
                            int gray = (col.Item1 + col.Item2 + col.Item3) / 9;
                            col = (gray, gray, gray);
                        }
                        float2 q = img(i, j);
                        cv.Set((int)q.x, (int)q.y, col);
                    }
                // drainages blue, spurs orange (2 ha and up)
                float cell2 = g.CellM * g.CellM;
                for (int c = 0; flow && c < g.W * g.D; c++)
                {
                    if (!g.In[c])
                        continue;
                    float2 q = fromWorld(g.World(c));
                    if (acc[c] * cell2 >= ChannelM2)
                        cv.Disc(q.x, q.y, 1.2f, (60, 140, 255));
                    else if (accR[c] * cell2 >= ChannelM2)
                        cv.Disc(q.x, q.y, 0.8f, (230, 150, 60));
                }
                for (int k = 0; k < ring.Length; k++)
                    Stroke(cv, new List<float2> { fromWorld(ring[k]), fromWorld(ring[(k + 1) % ring.Length]) }, 1f, (255, 255, 255));
                return cv;
            }

            Canvas clean = Base(false);
            Program.WritePngRgb(Path.ChangeExtension(path, null) + "-draw.png", clean.W, clean.H, clean.Rgb);
            // pixel -> world for the -draw.png (and either panel of <id>.png): tools/rf-pix2world.cjs reads it
            File.WriteAllText(Path.ChangeExtension(path, null) + "-draw.map", string.Format(CultureInfo.InvariantCulture,
                "{{\"rot\":{0},\"w\":{1},\"d\":{2},\"ox\":{3},\"oz\":{4},\"x0\":{5},\"z0\":{6},\"t\":{7},\"panelW\":{8},\"gap\":12}}\n",
                rot, w, d, s.Origin.x, s.Origin.y, s.X0, s.Z0, t, iw));
            Canvas left = Base(true), right = Base(true);
            // left: the v2 plan (runs by band, lift chords yellow)
            foreach (AutoRun r in v2.Runs)
            {
                var pl = new List<float2>();
                foreach (float2 p in r.Centerline)
                    pl.Add(fromWorld(p));
                Stroke(left, pl, 4f, EdgeColor(r.Band));
                Stroke(left, pl, 2.5f, BandColor(r.Band));
            }
            if (v2.Resort?.Lifts != null)
                foreach (PlanLift lf in v2.Resort.Lifts)
                {
                    var pl = new List<float2> { fromWorld(lf.Bottom.xz), fromWorld(lf.Top.xz) };
                    Stroke(left, pl, 2f, (255, 220, 0));
                    left.Disc(pl[0].x, pl[0].y, 5f, (255, 220, 0));
                    left.Disc(pl[1].x, pl[1].y, 5f, (255, 220, 0));
                }
            // right: every candidate thin, picked lines thick by band, numbered at the top
            foreach (Line l in lines)
                if (!l.Picked)
                    Stroke(right, Img(l, fromWorld), 0.6f, (120, 200, 210));
            // right: the plan as scorecard v3 reads it:
            // - runs solid by the ground under them
            // - graded tracks dashed: a green cat track light green, a diagonal run (graded steeper than the blue line) in its band's color with a white edge
            // - picked lines no lift serves gray
            void Track(List<float2> tr)
            {
                var (run, band) = TrackClass(g, tr);
                List<float2> pl = tr.ConvertAll(p => fromWorld(p));
                if (run)
                {
                    // a diagonal run (a graded track at a run's grade): thinner than a run, striped white and its band's color
                    Stroke(right, pl, 3.2f, (255, 255, 255));
                    Dashed(right, pl, 1.9f, BandColor(band), 9f, 4f);
                }
                else
                {
                    // a green cat track: dotted
                    Dashed(right, pl, 2.4f, (20, 60, 20), 8f, 4f);
                    Dashed(right, pl, 1.4f, (150, 255, 120), 8f, 4f);
                }
            }
            foreach (List<float2> tr in built.Connectors)
                Track(tr);
            foreach (PlanRun r in built.Runs)
            {
                if (r.Graded)
                {
                    Track(r.Pts);
                    continue;
                }
                List<float2> pl = r.Pts.ConvertAll(p => fromWorld(p));
                if (r.Src != null && r.Src.Lift < 0)
                {
                    Stroke(right, pl, 2f, (150, 150, 150));
                    continue;
                }
                SlopeBand band = GroundBand(g, r.Pts);
                if (r.Surface == RunKind.Natural)
                {
                    // a natural (ungroomed) run: dashed
                    Dashed(right, pl, 4.5f, EdgeColor(band), 12f, 6f);
                    Dashed(right, pl, 3f, BandColor(band), 12f, 6f);
                    continue;
                }
                Stroke(right, pl, 4.5f, EdgeColor(band));
                Stroke(right, pl, 3f, BandColor(band));
            }
            foreach (PlanRun r in built.Runs)
                if (r.Src == null || r.Src.Lift >= 0)
                {
                    float2 p0 = fromWorld(r.Pts[0]);
                    right.Disc(p0.x, p0.y, 6f, (255, 255, 255));
                    if (r.Src != null && r.Src.Pick > 0)
                        Label(right, (int)p0.x + 8, (int)p0.y - 6, r.Src.Pick);
                }

            foreach (var (p, cr, cg, cb) in built.Dots)
            {
                float2 q = fromWorld(p);
                right.Disc(q.x, q.y, 2f, (cr, cg, cb));
            }
            foreach (Lift lf in lifts)
            {
                var pl = new List<float2> { fromWorld(lf.Bottom.xz), fromWorld(lf.Top.xz) };
                // chairs yellow, a gondola cyan, a tram red, a beginner chair light green
                (int, int, int) rope = lf.Type switch { "gondola" => (0, 220, 255), "tram" => (255, 70, 70), "beginner" => (150, 255, 120), _ => (255, 220, 0) };
                Stroke(right, pl, 3f, (0, 0, 0));
                Stroke(right, pl, 2f, rope);
                right.Disc(pl[0].x, pl[0].y, 7f, (0, 0, 0));
                right.Disc(pl[0].x, pl[0].y, 5.5f, rope);
                right.Disc(pl[1].x, pl[1].y, 7f, (0, 0, 0));
                right.Disc(pl[1].x, pl[1].y, 5.5f, rope);
                float2 mid = math.lerp(pl[0], pl[1], 0.5f);
                Label(right, (int)mid.x + 6, (int)mid.y - 6, lf.Id, (255, 220, 0), (0, 0, 0));
            }
            var both = new Canvas(iw * 2 + 12, ih);
            for (int y = 0; y < ih; y++)
            {
                Array.Copy(left.Rgb, y * iw * 3, both.Rgb, y * both.W * 3, iw * 3);
                Array.Copy(right.Rgb, y * iw * 3, both.Rgb, (y * both.W + iw + 12) * 3, iw * 3);
            }
            Program.WritePngRgb(path, both.W, both.H, both.Rgb);
            string[] ups = { "north (+z)", "east (+x)", "south (-z)", "west (-x)" };
            Console.WriteLine($"REPORT rf render {Path.GetFileName(path)}: {both.W} x {both.H}, image top = {ups[rot]}, 1 px = {t:F1} m; left v2, right runs-first");
        }

        private static List<float2> Img(Line l, Func<float2, float2> fromWorld)
        {
            var pl = new List<float2>(l.Pts.Count);
            foreach (float3 p in l.Pts)
                pl.Add(fromWorld(p.xz));
            return pl;
        }

        private static void Dashed(Canvas cv, List<float2> pl, float r, (int, int, int) c, float period = 10f, float on = 6f)
        {
            float run = 0f;
            for (int i = 1; i < pl.Count; i++)
            {
                float len = math.distance(pl[i - 1], pl[i]);
                for (float u = 0; u <= len; u += 0.5f, run += 0.5f)
                    if (run % period < on)
                    {
                        float2 p = math.lerp(pl[i - 1], pl[i], len > 0 ? u / len : 0);
                        cv.Disc(p.x, p.y, r, c);
                    }
            }
        }

        private static void Stroke(Canvas cv, List<float2> pl, float r, (int, int, int) c)
        {
            for (int i = 1; i < pl.Count; i++)
            {
                float len = math.distance(pl[i - 1], pl[i]);
                for (float u = 0; u <= len; u += 0.5f)
                {
                    float2 p = math.lerp(pl[i - 1], pl[i], len > 0 ? u / len : 0);
                    cv.Disc(p.x, p.y, r, c);
                }
            }
        }

        private static void Label(Canvas cv, int x, int y, int n) => Label(cv, x, y, n, (0, 0, 0), (255, 255, 255));

        private static void Label(Canvas cv, int x, int y, int n, (int, int, int) bg, (int, int, int) fg)
        {
            string s = n.ToString(CultureInfo.InvariantCulture);
            const int sc = 3;
            for (int k = 0; k < s.Length; k++)
            {
                string glyph = Digits[s[k] - '0'];
                for (int gy = -1; gy <= 5; gy++)
                    for (int gx = -1; gx <= 3; gx++)
                        for (int py = 0; py < sc; py++)
                            for (int px = 0; px < sc; px++)
                                cv.Set(x + k * 4 * sc + gx * sc + px, y + gy * sc + py, bg);
                for (int gy = 0; gy < 5; gy++)
                    for (int gx = 0; gx < 3; gx++)
                        if (glyph[gy * 3 + gx] == '1')
                            for (int py = 0; py < sc; py++)
                                for (int px = 0; px < sc; px++)
                                    cv.Set(x + k * 4 * sc + gx * sc + px, y + gy * sc + py, fg);
            }
        }

        private static void WriteLifts(string path, string id, List<Lift> lifts, List<Line> lines)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"# lifts {id}: lift type length_m rise_m bottom_grade new_vertical_m transfer lines(map#:band) traverses");
            foreach (Lift lf in lifts)
            {
                var ls = new List<string>();
                foreach (Line l in lf.Lines)
                    ls.Add($"{l.Pick}:{l.Band}");
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "L{0} {1} {2:F0} {3:F0} {4:F2} {5:F0} {8:F0} [{6}] {7}",
                    lf.Id, lf.Type, lf.Length, lf.Rise, lf.BottomGrade, lf.Gain, string.Join(" ", ls), lf.Traverses.Count, lf.Transfer));
            }
            int picked = 0, unserved = 0;
            foreach (Line l in lines)
                if (l.Picked)
                {
                    picked++;
                    if (l.Lift < 0)
                        unserved++;
                }
            sb.AppendLine($"# {unserved} of {picked} picked line(s) served by no lift");
            File.AppendAllText(path, sb.ToString());
            Console.WriteLine($"REPORT rf lifts {id}: {lifts.Count} lift(s) [{string.Join(", ", lifts.ConvertAll(l => $"L{l.Id} {l.Type} {l.Length:F0} m / {l.Rise:F0} m up, {l.Lines.Count} line(s), {l.Traverses.Count} traverse(s){(l.Transfer > 0 ? ", feeds another lift" : "")}"))}]; {unserved} of {picked} line(s) unserved");
        }

        /// <summary>RF_V3DBG: the plan's elements by their v3 class, and the crowded samples (the scorecard's rule, served or not) by the
        /// kinds of the two elements.</summary>
        private void V3Debug(Grid g, PlanDraft plan, List<Lift> lifts)
        {
            var els = new List<(string Kind, float3[] P, bool Run)>();
            foreach (PlanRun r in plan.Runs)
                els.Add(("run:" + r.Kind, Sampled(g, r.Pts).ToArray(), true));
            int[] cls = new int[6];
            float[] km = new float[6];
            foreach (List<float2> c in plan.Connectors)
            {
                var (run, band) = TrackClass(g, c);
                int k = run ? (int)band : 0;
                cls[k]++;
                km[k] += RunLength(c) / 1000f;
                els.Add((run ? "graded-" + band : "cat", Sampled(g, c).ToArray(), run));
            }
            Console.WriteLine($"V3 connectors: green cat {cls[0]} ({km[0]:F1} km), diagonal blue {cls[2]} ({km[2]:F1} km), black {cls[3]} ({km[3]:F1} km), " +
                $"double black {cls[4]} ({km[4]:F1} km); runs by ground: " +
                string.Join(" ", Enumerable.Range(1, 4).Select(b => $"{(SlopeBand)b} {plan.Runs.Count(r => GroundBand(g, r.Pts) == (SlopeBand)b)}")) +
                $"; graded stubs (< 50 m drop) {els.Count(e => e.Run && e.Kind.StartsWith("graded") && e.P[0].y - e.P[e.P.Length - 1].y < 50f)}");
            var by = new Dictionary<string, int>();
            int pts = 0, crowded = 0;
            for (int i = 0; i < els.Count; i++)
            {
                if (!els[i].Run)
                    continue;
                float3[] P = els[i].P;
                for (int k = 0; k < P.Length - 15; k++)
                {
                    pts++;
                    for (int j = 0; j < els.Count; j++)
                    {
                        if (j == i || !els[j].Run)
                            continue;
                        float3[] Q = els[j].P;
                        if (math.distancesq(Q[0].xz, P[k].xz) < 1e4f || math.distancesq(Q[Q.Length - 1].xz, P[k].xz) < 1e4f)
                            continue;
                        if (Q.Any(q => math.distancesq(q.xz, P[k].xz) < 1600f))
                        {
                            crowded++;
                            string key = els[i].Kind.Split(':')[0].Replace("graded-Blue", "graded").Replace("graded-Black", "graded").Replace("graded-DoubleBlack", "graded")
                                + " by " + els[j].Kind.Split(':')[0].Replace("graded-Blue", "graded").Replace("graded-Black", "graded").Replace("graded-DoubleBlack", "graded");
                            by[key] = by.TryGetValue(key, out int v) ? v + 1 : 1;
                            break;
                        }
                    }
                }
            }
            Console.WriteLine($"V3 crowding: {crowded} of {pts} samples ({(pts > 0 ? crowded / (float)pts : 0f):P0}); " + string.Join(", ", by.OrderByDescending(kv => kv.Value).Select(kv => kv.Key + " " + kv.Value)));
            // the graded pieces: length, drop, along-path grade, link or traverse tree, and the crowded samples they take part in
            var links = new HashSet<List<float2>>(plan.Links);
            var rows = new List<(int Hits, string Text)>();
            for (int ci = 0; ci < plan.Connectors.Count; ci++)
            {
                int e = plan.Runs.Count + ci;
                if (!els[e].Run)
                    continue;
                float3[] Q = els[e].P;
                int hits = 0;
                for (int i = 0; i < els.Count; i++)
                {
                    if (i == e || !els[i].Run)
                        continue;
                    float3[] P = els[i].P;
                    for (int k = 0; k < P.Length - 15; k++)
                        if (math.distancesq(Q[0].xz, P[k].xz) >= 1e4f && math.distancesq(Q[Q.Length - 1].xz, P[k].xz) >= 1e4f && Q.Any(q => math.distancesq(q.xz, P[k].xz) < 1600f))
                            hits++;
                    for (int k = 0; k < Q.Length - 15; k++)
                        if (math.distancesq(P[0].xz, Q[k].xz) >= 1e4f && math.distancesq(P[P.Length - 1].xz, Q[k].xz) >= 1e4f && P.Any(q => math.distancesq(q.xz, Q[k].xz) < 1600f))
                            hits++;
                }
                List<float2> c = plan.Connectors[ci];
                rows.Add((hits, $"{(links.Contains(c) ? "link" : "tree")} {RunLength(c):F0} m, {Q[0].y - Q[Q.Length - 1].y:F0} m down, along {AlongGrade(g, c):F2}, crowd {hits}, from {c[0].x:F0},{c[0].y:F0} to {c[c.Count - 1].x:F0},{c[c.Count - 1].y:F0}"));
            }
            foreach (var row in rows.OrderByDescending(r => r.Hits).Take(O.DebugV3All ? 999 : 12))
                Console.WriteLine("V3 graded " + row.Text);
        }

        private static void TurnDebug(string id, PlanDraft plan)
        {
            var rows = new List<(int N, string S)>();
            foreach (PlanRun r in plan.Runs.Where(r => r.Graded))
                rows.Add((SharpTurns(r.Pts), $"run {r.Kind} {RunLength(r.Pts):F0} m from {r.Pts[0].x:F0},{r.Pts[0].y:F0}"));
            foreach (List<float2> c in plan.Connectors)
                rows.Add((SharpTurns(c), $"{(plan.Links.Contains(c) ? "link/way" : "track")} {RunLength(c):F0} m from {c[0].x:F0},{c[0].y:F0} to {c[c.Count - 1].x:F0},{c[c.Count - 1].y:F0}"));
            foreach (var (n, s) in rows.Where(x => x.N > 0).OrderByDescending(x => x.N).Take(12))
                Console.WriteLine($"TURNS {id}: {n} {s}");
            Console.WriteLine($"TURNS {id}: total {rows.Sum(x => x.N)} in {rows.Count} track(s)");
        }
    }
}
