// The lift tool's section pick by the rope (DECISIONS 591, F1, docs/research/tram-bulldoze-hitbox-2026-11-03.md): the pure
// LiftRopePick (ray vs rope polyline within a screen-space tolerance). Where-tags `rope/RP-n`.
// Ropes are built the way LaneSystem hangs a utility lane (LaneSystem.cs:4128-4138: b, c re-chorded at 1/3 and 2/3 and lowered by
// L x h x 4/3, so the mid-span sag is L x h). The field tram span (field-test.md section C) is the anchor. Rope ends 804.47 /
// 1001.69 over 473 m at h 0.04 give the measured mid-span rope 884.16. The bulldozer's sheet sat at 894.05 there.
// Pinned:
// - The ray through the visible rope picks its section (mid-span and near a tower, 473 m and 1 km tram spans, FG / DQ / GD chains).
// - A ray at the old sheet height picks nothing, and never a far section.
// - The tolerance edge is exact (screen share and the near-camera floor).
// - Of two parallel lines, the nearer one wins when both overlap on screen. Otherwise the one under the cursor wins.
// - The pick does not choose a rope behind the terrain hit.
// - The polyline follows a 1 km rope within 0.02 m.
// - The box cull keeps the cost to the ropes near the ray.

using System;
using System.Collections.Generic;
using SkiResortMod.Systems;
using Unity.Mathematics;

namespace LiftBatch
{
    internal static class RopePickTests
    {
        private static Func<bool, string, string, bool> s_Check;

        private static bool Check(bool ok, string where, string msg) => s_Check(ok, where, msg);

        // tan(30 deg): a 60 deg vertical view. The tolerance is a screen share, so any fov gives the same picks in pixels.
        private static readonly float FovTan = math.tan(math.radians(30f));
        private const float RayLen = 20000f;

        public static void Run(Func<bool, string, string, bool> check)
        {
            s_Check = check;
            Rp1TramFieldSpan();
            Rp2TramKilometer();
            Rp3SheetNeverPicksFar();
            Rp4ToleranceEdges();
            Rp5ParallelLines();
            Rp6ChairChains();
            Rp7BehindTerrain();
            Rp8PolylineAccuracy();
            Rp9Cost();
        }

        /// <summary>A scene of sections, each one or more ropes.</summary>
        private sealed class Scene
        {
            public readonly List<float3> Pts = new List<float3>();
            public readonly List<LiftRopePick.Rope> Ropes = new List<LiftRopePick.Rope>();
            public readonly List<LiftRopePick.Hit> Scratch = new List<LiftRopePick.Hit>();
            public int Sections;

            /// <summary>A hanging rope between rope ends a and d (LaneSystem's shape) for section s.</summary>
            public void Rope(int s, float3 a, float3 d, float h)
            {
                float3 b = math.lerp(a, d, 1f / 3f), c = math.lerp(a, d, 2f / 3f);
                float drop = math.distance(a.xz, d.xz) * h * (4f / 3f);
                b.y -= drop;
                c.y -= drop;
                Ropes.Add(LiftRopePick.AddRope(Pts, s, a, b, c, d));
                Sections = math.max(Sections, s + 1);
            }

            /// <summary>A section with two ropes at +-half (the up and down cable), offset across the line in xz.</summary>
            public void Pair(int s, float3 a, float3 d, float h, float half)
            {
                float3 dir = math.normalize(new float3(d.x - a.x, 0f, d.z - a.z));
                float3 right = new float3(dir.z, 0f, -dir.x) * half;
                Rope(s, a + right, d + right, h);
                Rope(s, a - right, d - right, h);
            }

            public bool Pick(float3 cam, float3 at, out LiftRopePick.Hit hit, float maxCam = float.PositiveInfinity, List<bool> on = null) =>
                LiftRopePick.Pick(Pts, Ropes, on, cam, cam + math.normalize(at - cam) * RayLen, FovTan, maxCam, Scratch, out hit, out _);
        }

        /// <summary>The rope point at fraction t of section s's first rope (the exact curve, not the polyline).</summary>
        private static float3 RopeAt(float3 a, float3 d, float h, float t)
        {
            float3 b = math.lerp(a, d, 1f / 3f), c = math.lerp(a, d, 2f / 3f);
            float drop = math.distance(a.xz, d.xz) * h * (4f / 3f);
            b.y -= drop;
            c.y -= drop;
            return LiftRopePick.Bezier(a, b, c, d, t);
        }

        private static string F(float v) => v.ToString("F2");

        // The field span: tower T45 rope 804.47 at z 0 -> top station rope 1001.69 at z 473, h 0.04. A lower section lies 300 m below the tower.
        private static readonly float3 FieldA = new float3(0f, 804.47f, 0f), FieldD = new float3(0f, 1001.69f, 473f);
        private static readonly float3 LowerA = new float3(0f, 700f, -300f);

        private static void Rp1TramFieldSpan()
        {
            const string W = "rope/RP-1";
            var sc = new Scene();
            sc.Rope(0, FieldA, FieldD, 0.04f);
            sc.Rope(1, LowerA, FieldA, 0.04f);
            float3 mid = RopeAt(FieldA, FieldD, 0.04f, 0.5f);
            Check(math.abs(mid.y - 884.16f) < 0.02f, W, "the field span's mid-span rope is " + F(mid.y) + ", measured 884.16");
            // Side-on from 400 m, at rope height.
            float3 cam = new float3(-400f, mid.y, mid.z);
            Check(sc.Pick(cam, mid, out LiftRopePick.Hit h) && h.Section == 0 && h.MissM < 0.05f, W,
                  "the ray through the mid-span rope picks section 0 (got " + h.Section + ", miss " + F(h.MissM) + ")");
            // The same from above at 45 deg and along the line at 30 deg: the pick is not tied to a side-on view.
            float3 above = mid + new float3(-300f, 300f, 0f), along = mid + new float3(-50f, 250f, -430f);
            Check(sc.Pick(above, mid, out h) && h.Section == 0 && h.MissM < 0.05f, W, "45 deg above: section " + h.Section + " miss " + F(h.MissM));
            Check(sc.Pick(along, mid, out h) && h.Section == 0 && h.MissM < 0.05f, W, "along the line: section " + h.Section + " miss " + F(h.MissM));
            // Near the tower, 2 % and 5 % into each section: the section the cursor is on, not its neighbor.
            foreach (float t in new[] { 0.02f, 0.05f })
            {
                float3 up = RopeAt(FieldA, FieldD, 0.04f, t), down = RopeAt(LowerA, FieldA, 0.04f, 1f - t);
                Check(sc.Pick(new float3(-400f, up.y, up.z), up, out h) && h.Section == 0, W,
                      "near the tower, " + (t * 100f) + " % into the upper span: section " + h.Section + " (miss " + F(h.MissM) + ")");
                Check(sc.Pick(new float3(-400f, down.y, down.z), down, out h) && h.Section == 1, W,
                      "near the tower, " + (t * 100f) + " % into the lower span: section " + h.Section + " (miss " + F(h.MissM) + ")");
            }
            // The bulldozer's sheet at mid-span, 894.05 (+9.9 m): no rope pick from 400 m (tolerance 5.8 m).
            float3 sheet = new float3(mid.x, 894.05f, mid.z);
            bool any = sc.Pick(cam, sheet, out h);
            Check(!any, W, "a ray at the old sheet (+9.9 m) picks nothing from 400 m; got section " + h.Section + " miss " + F(h.MissM) +
                           " tolerance " + F(h.TolM));
        }

        private static void Rp2TramKilometer()
        {
            const string W = "rope/RP-2";
            var sc = new Scene();
            float3 a = new float3(0f, 800f, 0f), d = new float3(0f, 1100f, 1000f);
            sc.Pair(0, a, d, 0.04f, 5.5f);   // the track ropes
            sc.Pair(0, a + new float3(0f, -0.4f, 0f), d + new float3(0f, -0.4f, 0f), 0.04f, 5f);   // the haul ropes
            float3 mid = RopeAt(a + new float3(5.5f, 0f, 0f), d + new float3(5.5f, 0f, 0f), 0.04f, 0.5f);
            Check(math.abs(mid.y - (950f - 40f)) < 0.05f, W, "1 km at h 0.04 sags 40 m mid-span: rope " + F(mid.y));
            float3 cam = new float3(600f, mid.y + 150f, 450f);
            Check(sc.Pick(cam, mid, out LiftRopePick.Hit h) && h.Section == 0 && h.MissM < 0.1f, W,
                  "1 km span, mid-span rope from about 620 m: section " + h.Section + ", miss " + F(h.MissM));
            // Term B at 1 km is 13.3 m (L x h / 3): the old sheet is out of a 9 m tolerance.
            Check(!sc.Pick(cam, mid + new float3(0f, 13.3f, 0f), out h), W,
                  "a ray 13.3 m above the 1 km rope picks nothing; got miss " + F(h.MissM) + " tolerance " + F(h.TolM));
            // Any point along the span from 600 m off the line.
            int misses = 0;
            for (int i = 1; i < 20; i++)
            {
                float3 p = RopeAt(a + new float3(-5.5f, 0f, 0f), d + new float3(-5.5f, 0f, 0f), 0.04f, i / 20f);
                if (!sc.Pick(new float3(-600f, p.y + 100f, p.z), p, out h) || h.Section != 0 || h.MissM > 0.1f)
                    misses++;
            }
            Check(misses == 0, W, misses + " of 19 points along the 1 km span not picked dead on");
        }

        private static void Rp3SheetNeverPicksFar()
        {
            const string W = "rope/RP-3";
            // Two tram sections in a line: A (field span) and B beyond it, 600 m further up. The camera looks up the line from below.
            var sc = new Scene();
            sc.Rope(0, FieldA, FieldD, 0.04f);
            float3 farD = FieldD + new float3(0f, 200f, 600f);
            sc.Rope(1, FieldD, farD, 0.04f);
            float3 cam = new float3(-120f, 860f, -250f);
            float3 midA = RopeAt(FieldA, FieldD, 0.04f, 0.5f);
            float3 sheetA = midA + new float3(0f, 9.9f, 0f);
            // Looking up the line the sheet point lies about 10 px from A's rope on screen (5.5 m at 500 m). A may answer. B never does.
            bool any = sc.Pick(cam, sheetA, out LiftRopePick.Hit h);
            Check(!any || (h.Section == 0 && h.Score <= 1f), W, "a ray at A's old sheet picks the far section " + h.Section + " (miss " +
                  F(h.MissM) + ")");
            // At A's rope: A. At B's rope: B.
            Check(sc.Pick(cam, midA, out h) && h.Section == 0, W, "the ray at A's rope picks A, got " + h.Section);
            float3 midB = RopeAt(FieldD, farD, 0.04f, 0.5f);
            Check(sc.Pick(cam, midB, out h) && h.Section == 1, W, "the ray at B's rope picks B, got " + h.Section);

            // Side-on, a far parallel span C 700 m behind A lies 25 m under the ray through A's sheet point. C's tolerance there (1.1 km
            // from the camera) is 15.9 m. The pick finds nothing. A world tolerance of 25 m or more would hand the click to C.
            var side = new Scene();
            side.Rope(0, FieldA, FieldD, 0.04f);
            float3 sideCam = new float3(-400f, midA.y, midA.z);
            float rise = 9.9f / 400f;   // the ray's climb per meter along x
            float3 cOff = new float3(700f, 9.9f + rise * 700f - 25f, 0f);
            side.Rope(1, FieldA + cOff, FieldD + cOff, 0.04f);
            bool sheet = side.Pick(sideCam, sheetA, out h);
            Check(!sheet, W, "side-on, a ray at A's old sheet picks nothing (A 9.9 m off, C 25 m off); got section " + h.Section + " miss " +
                  F(h.MissM) + " of " + F(h.TolM));
            Check(side.Pick(sideCam, midA, out h) && h.Section == 0, W, "side-on, the ray at A's rope picks A, got " + h.Section);
        }

        private static void Rp4ToleranceEdges()
        {
            const string W = "rope/RP-4";
            // A flat FG span (90 m, h 0.02) along +z: at mid-span the rope is level, so a vertical offset is the miss.
            var sc = new Scene();
            float3 a = new float3(0f, 100f, 0f), d = new float3(0f, 100f, 90f);
            sc.Rope(0, a, d, 0.02f);
            float3 mid = RopeAt(a, d, 0.02f, 0.5f);
            foreach (float dist in new[] { 200f, 1500f, 20f })
            {
                float3 cam = new float3(-dist, mid.y, mid.z);
                float tol = LiftRopePick.Tolerance(dist, FovTan);
                bool inside = sc.Pick(cam, mid + new float3(0f, 0.97f * tol, 0f), out LiftRopePick.Hit h);
                bool outside = sc.Pick(cam, mid + new float3(0f, 1.03f * tol, 0f), out LiftRopePick.Hit o);
                Check(inside && h.Score <= 1f && h.Score > 0.9f, W, dist + " m: 0.97 x tolerance (" + F(tol) + " m) is in; score " + F(h.Score));
                Check(!outside, W, dist + " m: 1.03 x tolerance is out; got miss " + F(o.MissM) + " of " + F(o.TolM));
            }
            Check(math.abs(LiftRopePick.Tolerance(20f, FovTan) - LiftRopePick.MinTolM) < 1e-6f, W, "20 m from the camera the floor holds: " +
                  F(LiftRopePick.Tolerance(20f, FovTan)));
            Check(math.abs(LiftRopePick.Tolerance(400f, FovTan) - 400f * FovTan * LiftRopePick.ScreenFrac) < 1e-4f, W,
                  "400 m: the screen share, " + F(LiftRopePick.Tolerance(400f, FovTan)) + " m");
        }

        private static void Rp5ParallelLines()
        {
            const string W = "rope/RP-5";
            // Two FG lines 12 m apart (x 0 and 12), the same ground.
            var sc = new Scene();
            float3 a = new float3(0f, 100f, 0f), d = new float3(0f, 130f, 90f), off = new float3(12f, 0f, 0f);
            sc.Rope(0, a, d, 0.02f);
            sc.Rope(1, a + off, d + off, 0.02f);
            float3 midA = RopeAt(a, d, 0.02f, 0.5f), midB = midA + off;
            // Side-on from x -200: both ropes on one screen line, B right behind A.
            Check(sc.Pick(new float3(-200f, midA.y, midA.z), midA, out LiftRopePick.Hit h) && h.Section == 0, W,
                  "overlapping on screen, the nearer (A) wins; got " + h.Section);
            Check(sc.Pick(new float3(212f, midA.y, midA.z), midB, out h) && h.Section == 1, W,
                  "from the other side the nearer is B; got " + h.Section);
            // From 45 deg above: the two ropes apart on screen, the one under the cursor wins.
            float3 cam = midB + new float3(-100f, 100f, 0f);
            Check(sc.Pick(cam, midB, out h) && h.Section == 1, W, "45 deg above, aimed at the far rope B: " + h.Section);
            Check(sc.Pick(midA + new float3(-100f, 100f, 0f), midA, out h) && h.Section == 0, W, "45 deg above, aimed at A: " + h.Section);
            // Top-down from 400 m, the cursor between them, 4.6 m from A: A (B is 7.4 m off, past the 5.8 m tolerance).
            float3 between = midA + new float3(4.6f, 0f, 0f);
            Check(sc.Pick(between + new float3(0f, 400f, 0f), between, out h) && h.Section == 0, W, "top-down, nearer to A: " + h.Section);
            float3 nearB = midA + new float3(8f, 0f, 0f);
            Check(sc.Pick(nearB + new float3(0f, 400f, 0f), nearB, out h) && h.Section == 1, W, "top-down, nearer to B: " + h.Section);
            // A section switched off (another lift type than the tool's) is never picked, even dead on.
            var on = new List<bool> { false, true };
            Check(!sc.Pick(new float3(-200f, midA.y + 100f, midA.z), midA, out h, float.PositiveInfinity, on) || h.Section == 1, W,
                  "a disabled section is skipped; got " + h.Section);
        }

        private static void Rp6ChairChains()
        {
            const string W = "rope/RP-6";
            // Up-slope chains at the span limits: FG 90 m (h 0.02), DQ and GD 245 m (0.025). Each section has the up and down cable.
            var cases = new[] { ("FG", 90f, 0.02f, 3f), ("DQ", 245f, 0.025f, 3.2f), ("GD", 245f, 0.025f, 3.5f) };
            foreach (var (name, span, h, half) in cases)
            {
                var sc = new Scene();
                var ends = new List<float3>();
                for (int k = 0; k <= 4; k++)
                    ends.Add(new float3(0f, 1000f + 0.35f * span * k, span * k));
                for (int k = 0; k < 4; k++)
                    sc.Pair(k, ends[k], ends[k + 1], h, half);
                int bad = 0;
                string first = null;
                for (int k = 0; k < 4; k++)
                {
                    foreach (float t in new[] { 0.5f, 0.06f, 0.94f })
                    {
                        float3 p = RopeAt(ends[k] + new float3(half, 0f, 0f), ends[k + 1] + new float3(half, 0f, 0f), h, t);
                        foreach (float3 cam in new[] { p + new float3(-250f, 60f, 0f), p + new float3(-150f, 200f, -150f) })
                        {
                            if (sc.Pick(cam, p, out LiftRopePick.Hit hit) && hit.Section == k && hit.MissM < 0.05f)
                                continue;
                            bad++;
                            first = first ?? (name + " section " + k + " t " + t + ": got " + hit.Section + " miss " + F(hit.MissM));
                        }
                    }
                }
                Check(bad == 0, W, name + ": " + bad + " of 24 rays picked another section or missed; first " + first);
            }
        }

        private static void Rp7BehindTerrain()
        {
            const string W = "rope/RP-7";
            // A rope seen through a hill: the terrain hit (maxCam) lies 300 m before it along the ray.
            var sc = new Scene();
            sc.Rope(0, FieldA, FieldD, 0.04f);
            float3 mid = RopeAt(FieldA, FieldD, 0.04f, 0.5f);
            float3 cam = new float3(-800f, mid.y + 50f, mid.z);
            float dist = math.distance(cam, mid);
            Check(!sc.Pick(cam, mid, out LiftRopePick.Hit h, dist - 300f), W, "a rope behind the terrain hit is not picked; got miss " + F(h.MissM));
            Check(sc.Pick(cam, mid, out h, dist - 2f), W, "a rope 2 m past the terrain hit (within the tolerance) still is");
            Check(sc.Pick(cam, mid, out h, dist + 200f), W, "a rope in front of the terrain hit is picked");
        }

        private static void Rp8PolylineAccuracy()
        {
            const string W = "rope/RP-8";
            float3 a = new float3(0f, 800f, 0f), d = new float3(0f, 1100f, 1000f);
            var sc = new Scene();
            sc.Rope(0, a, d, 0.04f);
            LiftRopePick.Rope r = sc.Ropes[0];
            Check(r.Count == LiftRopePick.MaxSegments + 1, W, "1 km rope: " + (r.Count - 1) + " segments");
            float worst = 0f;
            for (int i = 0; i <= 2000; i++)
            {
                float3 p = RopeAt(a, d, 0.04f, i / 2000f);
                float best = float.PositiveInfinity;
                for (int k = 0; k < r.Count - 1; k++)
                    best = math.min(best, LiftRopePick.SegmentRay(sc.Pts[r.First + k], sc.Pts[r.First + k + 1], p, new float3(0f, 1f, 0f), 0f, out _, out _));
                worst = math.max(worst, best);
            }
            Check(worst < 0.02f, W, "the polyline stays within 0.02 m of the 1 km curve; worst " + worst.ToString("F4"));
            Check(LiftRopePick.Segments(5f) == LiftRopePick.MinSegments && LiftRopePick.Segments(245f) == 25, W,
                  "segments: 5 m -> " + LiftRopePick.Segments(5f) + ", 245 m -> " + LiftRopePick.Segments(245f));
        }

        private static void Rp9Cost()
        {
            const string W = "rope/RP-9";
            // A large resort: 40 FG lines of 10 sections (2 ropes each) on a 200 m grid. The ray at one rope tests only the ropes near it.
            var sc = new Scene();
            int s = 0;
            for (int line = 0; line < 40; line++)
            {
                float x = (line % 8) * 200f, z0 = (line / 8) * 1000f;
                for (int k = 0; k < 10; k++, s++)
                    sc.Pair(s, new float3(x, 1000f + 30f * k, z0 + 90f * k), new float3(x, 1030f + 30f * k, z0 + 90f * (k + 1)), 0.02f, 3f);
            }
            int total = 0;
            foreach (LiftRopePick.Rope r in sc.Ropes)
                total += r.Count - 1;
            float3 p = RopeAt(new float3(603f, 1120f, 2360f), new float3(603f, 1150f, 2450f), 0.02f, 0.5f);
            float3 cam = p + new float3(-300f, 300f, -200f);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            bool ok = LiftRopePick.Pick(sc.Pts, sc.Ropes, null, cam, cam + math.normalize(p - cam) * RayLen, FovTan, float.PositiveInfinity,
                sc.Scratch, out LiftRopePick.Hit h, out int tested);
            sw.Stop();
            Check(ok && h.Section == 19 * 10 + 4 && h.MissM < 0.05f, W, "the big resort picks line 19 section 4: got " + h.Section + " miss " + F(h.MissM));
            Check(tested * 20 < total, W, "the box cull tests " + tested + " of " + total + " segments (want under 5 %)");
            Console.WriteLine("ROPE  RP-9 cost: " + sc.Ropes.Count + " ropes, " + total + " segments, " + tested + " tested, " +
                              sw.Elapsed.TotalMilliseconds.ToString("F3") + " ms (first call, JIT included)");
        }
    }
}
