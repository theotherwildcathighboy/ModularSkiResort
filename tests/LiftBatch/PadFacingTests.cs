// The padded face uphill (DECISIONS 600 e). Where-tags `pad/...`.
// Every tower mesh has its crash pad and TrainFront half on local +z. Vanilla yaws a node tower along the draw direction
// (Game.Net/NodeAlignSystem.cs:85-111, :209-212). Therefore TowerLeanSystem turns it by 0 or 180 deg (LiftAlignMath.SectionUphill /
// PadUphillYaw) before the lean.
// PF-1 the rule: a section can be drawn either way (bottom to top, top to bottom, a bent plan, a hump, ground ends included). On it,
//      every tower's +z points toward the section's higher rope end. Both draw orders give the same world yaw. A flat section
//      (ends within PadFlatRiseM, or no finite rope height) keeps vanilla's yaw.
// PF-2 the ropes stay: every rope-carrying tower prefab in gamedata-mirror has an anchor set symmetric under the 180 deg turn (per lane
//      prefab). So the turned and leaned tower puts its anchors on the same world points as the unturned one (the turned frame is
//      Q x RotateY(pi)). The test matches anchors as LaneSystem.FindAnchors does (by position, sorted along the edge's lateral axis).
//      Each rope lane keeps its point and its side.
// PF-3 heads and trains on a turned tower: the rope rises toward +z on both halves (front = the uphill rope, LaneEndSide +1).
//      MastPitch reads +lean (the top tips downhill). The head relative to the mast plus the mast equals the rope bisector. The
//      halves keep their world pitch through PoseTrain, for either draw direction.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using SkiResortMod.Systems;
using Unity.Mathematics;

namespace LiftBatch
{
    public static class PadFacingTests
    {
        private static Func<bool, string, string, bool> s_Check;
        private static readonly List<string> s_Report = new List<string>();

        public static IReadOnlyList<string> Report => s_Report;

        public static void Run(Func<bool, string, string, bool> check)
        {
            s_Check = check;
            s_Report.Clear();
            Pf1Rule();
            Pf2RopesStay();
            Pf3HeadsAndTrains();
        }

        private static bool Check(bool ok, string where, string msg) => s_Check(ok, where, msg);

        private static string F(float v, string f) => v.ToString(f, CultureInfo.InvariantCulture);

        private static readonly float3 Z = new float3(0f, 0f, 1f);

        /// <summary>Vanilla's yaw at node i of a polyline drawn in list order: forward = the sum of the edges' travel tangents (the chords
        /// here), NodeAlignSystem.cs:85-111 / :209-212.</summary>
        private static quaternion VanillaYaw(IReadOnlyList<float3> p, int i)
        {
            float2 t = float2.zero;
            if (i > 0)
                t += math.normalize(p[i].xz - p[i - 1].xz);
            if (i < p.Count - 1)
                t += math.normalize(p[i + 1].xz - p[i].xz);
            return quaternion.LookRotationSafe(new float3(t.x, 0f, t.y), new float3(0f, 1f, 0f));
        }

        /// <summary>The padded face's world xz at every node of a section drawn in list order, rope heights at the ends ya / yb.</summary>
        private static List<float2> Faces(IReadOnlyList<float3> p, float ya, float yb)
        {
            var up = new List<float2>();
            LiftAlignMath.SectionUphill(p, ya, yb, up);
            var faces = new List<float2>();
            for (int i = 0; i < p.Count; i++)
                faces.Add(math.mul(LiftAlignMath.PadUphillYaw(VanillaYaw(p, i), up[i]), Z).xz);
            return faces;
        }

        private static List<float3> Reversed(List<float3> p)
        {
            var r = new List<float3>(p);
            r.Reverse();
            return r;
        }

        // ── PF-1: the rule ──────────────────────────────────────────────────────────────────────────────────────────────────────────
        private static void Pf1Rule()
        {
            const string W = "pad/PF-1";
            // A bent line rising toward +z (the smoke check's FG shape, 495 m), ground ends included.
            var line = new List<float3>();
            for (int i = 0; i <= 7; i++)
            {
                float s = i / 7f;
                line.Add(new float3(40f * math.sin(s * math.PI), 1000f + 300f * s, 2600f + 495f * s));
            }
            float yLow = 1000f + 8f, yHigh = 1300f + 8f;
            int cases = 0;
            foreach (bool topFirst in new[] { false, true })
            {
                List<float3> drawn = topFirst ? Reversed(line) : line;
                List<float2> faces = Faces(drawn, topFirst ? yHigh : yLow, topFirst ? yLow : yHigh);
                for (int i = 0; i < drawn.Count; i++)
                {
                    // uphill at node i: toward the next node in rising order. The top end points away from its lower neighbor
                    int k = topFirst ? drawn.Count - 1 - i : i;
                    float2 uphill = k < line.Count - 1 ? line[k + 1].xz - line[k].xz : line[k].xz - line[k - 1].xz;
                    Check(math.dot(faces[i], uphill) > 0f, W, (topFirst ? "top-first" : "bottom-first") + " node " + i + " faces " + faces[i] +
                          " against uphill " + uphill);
                    cases++;
                }
                // a vanilla tower on a top-first line faces downhill (the defect): the rule turns exactly those
                if (topFirst)
                    Check(math.dot(math.mul(VanillaYaw(drawn, 3), Z).xz, line[4].xz - line[3].xz) < 0f, W, "vanilla top-first tower already uphill?");
            }
            // either draw order: the same world yaw at every node
            List<float2> a = Faces(line, yLow, yHigh), b = Faces(Reversed(line), yHigh, yLow);
            float worst = 0f;
            for (int i = 0; i < line.Count; i++)
                worst = math.max(worst, math.length(a[i] - b[line.Count - 1 - i]));
            Check(worst < 1e-4f, W, "draw order changes a yaw by " + F(worst, "F5"));

            // a hump: the section's higher END decides for every tower (all face the same way, as on a real lift)
            var hump = new List<float3>
            {
                new float3(0f, 1000f, 0f), new float3(0f, 1080f, 100f), new float3(0f, 1120f, 200f), new float3(0f, 1060f, 300f),
                new float3(0f, 1040f, 400f),
            };
            foreach (bool rev in new[] { false, true })
            {
                List<float3> d = rev ? Reversed(hump) : hump;
                List<float2> f = Faces(d, rev ? 1048f : 1008f, rev ? 1008f : 1048f);
                for (int i = 0; i < d.Count; i++)
                    Check(f[i].y > 0.99f, W, "hump " + (rev ? "rev" : "fwd") + " node " + i + " faces " + f[i]);
            }

            // flat: ends within PadFlatRiseM (either sign) or not finite: vanilla's yaw everywhere
            foreach (float yb in new[] { 1008f + LiftAlignMath.PadFlatRiseM * 0.9f, 1008f - LiftAlignMath.PadFlatRiseM * 0.9f, float.NaN })
                foreach (bool rev in new[] { false, true })
                {
                    List<float3> d = rev ? Reversed(line) : line;
                    List<float2> f = Faces(d, 1008f, yb);
                    for (int i = 0; i < d.Count; i++)
                        Check(math.length(f[i] - math.mul(VanillaYaw(d, i), Z).xz) < 1e-5f, W, "flat (" + F(yb - 1008f, "F2") + ") node " + i +
                              " turned");
                }
            // PadUphillYaw: a zero direction keeps the yaw, and a perpendicular one keeps it. The turn is about vertical (y stays up)
            quaternion y0 = quaternion.RotateY(math.radians(37f));
            Check(LiftAlignMath.PadUphillYaw(y0, float2.zero).Equals(y0), W, "zero direction turned");
            float3 f0 = math.mul(y0, Z);
            Check(LiftAlignMath.PadUphillYaw(y0, new float2(f0.z, -f0.x)).Equals(y0), W, "perpendicular direction turned");
            quaternion t0 = LiftAlignMath.PadUphillYaw(y0, -f0.xz);
            Check(math.length(math.mul(t0, Z) + f0) < 1e-5f && math.length(math.mul(t0, new float3(0f, 1f, 0f)) - new float3(0f, 1f, 0f)) < 1e-5f,
                  W, "turn is not 180 about vertical");
            s_Report.Add("PF-1 " + cases + " towers on a bent 495 m line drawn both ways face its higher end; a hump faces the higher end; " +
                         "flat within " + F(LiftAlignMath.PadFlatRiseM, "F1") + " m keeps vanilla");
        }

        // ── PF-2: the ropes stay ────────────────────────────────────────────────────────────────────────────────────────────────────
        private struct Anchor
        {
            public string Lane;
            public float3 P;
        }

        private static void Pf2RopesStay()
        {
            const string W = "pad/PF-2";
            string probe = CatalogTests.RepoFile("gamedata-mirror/ImportedData/SkiLiftTowerDQ14/SkiLiftTowerDQ14.Prefab");
            if (!Check(probe != null, W, "gamedata-mirror tower prefabs not found"))
                return;
            string root = Path.GetDirectoryName(Path.GetDirectoryName(probe));
            int prefabs = 0;
            float worstSym = 0f, worstWorld = 0f;
            var names = new List<string>();
            foreach (string path in Directory.EnumerateFiles(root, "*.Prefab", SearchOption.AllDirectories))
            {
                string file = Path.GetFileNameWithoutExtension(path);
                if (!(file.StartsWith("SkiLiftTower", StringComparison.Ordinal) || file.StartsWith("SkiTramTower", StringComparison.Ordinal)) ||
                    file.Contains(" Mesh"))
                    continue;
                List<Anchor> anchors = Anchors(path);
                if (anchors.Count == 0)
                    continue;   // the placeholders: no rope
                prefabs++;
                string name = Regex.Replace(file, "_[0-9a-f]{32}$", string.Empty);
                names.Add(name);
                // (a) symmetric under the 180 deg turn about the prefab's y axis: (x, y, z) -> (-x, y, -z), per lane prefab
                foreach (Anchor an in anchors)
                {
                    float best = float.MaxValue;
                    foreach (Anchor other in anchors)
                        if (other.Lane == an.Lane)
                            best = math.min(best, math.length(other.P - new float3(-an.P.x, an.P.y, -an.P.z)));
                    worstSym = math.max(worstSym, best);
                    Check(best < 1e-3f, W, name + ": anchor " + an.P + " has no mirror (a 180 deg turn would move its rope " + F(best, "F3") + " m)");
                }
                // (b) the turned, leaned tower puts its anchors on the same world points, matched like FindAnchors, for leans both ways
                foreach (float pitch in new[] { 30f, -30f, 5f })
                {
                    quaternion yaw = quaternion.RotateY(math.radians(37f));
                    float lean = pitch == 5f ? 0f : 15f;
                    quaternion q = LiftAlignMath.LeanRotation(yaw, pitch, lean);
                    quaternion yawT = LiftAlignMath.PadUphillYaw(yaw, -math.mul(yaw, Z).xz);
                    quaternion qT = LiftAlignMath.LeanRotation(yawT, -pitch, lean);   // the same rope, read along the turned +z
                    float3 foot = new float3(-2400f, 1100f, 2900f), lateral = math.mul(yaw, new float3(1f, 0f, 0f));
                    worstWorld = math.max(worstWorld, Match(anchors, foot, q, qT, lateral, name, W));
                    Check(math.abs(math.dot(qT.value, math.mul(q, quaternion.RotateY(math.PI)).value)) > 0.99999f, W,
                          name + ": the turned frame is not Q x RotateY(pi) at pitch " + F(pitch, "F0"));
                }
            }
            Check(prefabs >= 30, W, "only " + prefabs + " rope-carrying tower prefabs found (expected the 32 members, twins and tram towers)");
            s_Report.Add("PF-2 " + prefabs + " tower prefabs: anchor sets symmetric under the turn (worst " + F(worstSym, "F4") +
                         " m), turned + leaned anchors on the same world points (worst " + F(worstWorld, "F5") + " m)");
        }

        /// <summary>Compares, per lane prefab, the world anchors of both poses pointwise, sorted along the edge's lateral axis
        /// (LaneSystem.FindAnchors' order). Returns the worst distance.</summary>
        private static float Match(List<Anchor> anchors, float3 foot, quaternion q, quaternion qT, float3 lateral, string name, string w)
        {
            float worst = 0f;
            var lanes = new HashSet<string>();
            foreach (Anchor a in anchors)
                lanes.Add(a.Lane);
            foreach (string lane in lanes)
            {
                var p = new List<float3>();
                var pT = new List<float3>();
                foreach (Anchor a in anchors)
                    if (a.Lane == lane)
                    {
                        p.Add(foot + math.mul(q, a.P));
                        pT.Add(foot + math.mul(qT, a.P));
                    }
                p.Sort((x, y) => math.dot(x, lateral).CompareTo(math.dot(y, lateral)));
                pT.Sort((x, y) => math.dot(x, lateral).CompareTo(math.dot(y, lateral)));
                for (int i = 0; i < p.Count; i++)
                {
                    float d = math.length(p[i] - pT[i]);
                    worst = math.max(worst, d);
                    Check(d < 1e-3f, w, name + ": rope " + i + " of lane " + lane + " moves " + F(d, "F4") + " m when the tower turns");
                }
            }
            return worst;
        }

        /// <summary>A tower prefab's sub-lane anchors (ObjectSubLanes rows: the curve's a, and d when the lane joins two node indices,
        /// as LaneSystem.FindAnchors reads them, Game.Net/LaneSystem.cs:2371-2418).</summary>
        private static List<Anchor> Anchors(string path)
        {
            var list = new List<Anchor>();
            string text = Regex.Replace(File.ReadAllText(path), "\\$fstrref:(\"[^\"]*\")", "$1");
            using (JsonDocument doc = JsonDocument.Parse(text))
            {
                if (!doc.RootElement.TryGetProperty("components", out JsonElement comps))
                    return list;
                foreach (JsonElement comp in comps.GetProperty("$rcontent").EnumerateArray())
                {
                    if (!comp.TryGetProperty("m_SubLanes", out JsonElement rows) || rows.ValueKind != JsonValueKind.Object)
                        continue;
                    foreach (JsonElement row in rows.GetProperty("$rcontent").EnumerateArray())
                    {
                        string lane = row.GetProperty("m_LanePrefab").GetString();
                        JsonElement curve = row.GetProperty("m_BezierCurve");
                        JsonElement ni = row.GetProperty("m_NodeIndex");
                        list.Add(new Anchor { Lane = lane, P = P(curve, "a") });
                        if (ni.GetProperty("x").GetInt32() != ni.GetProperty("y").GetInt32())
                            list.Add(new Anchor { Lane = lane, P = P(curve, "d") });
                    }
                }
            }
            return list;
        }

        private static float3 P(JsonElement curve, string pt)
        {
            JsonElement e = curve.GetProperty(pt);
            return new float3(e.GetProperty("x").GetSingle(), e.GetProperty("y").GetSingle(), e.GetProperty("z").GetSingle());
        }

        // ── PF-3: heads and trains on a turned tower ────────────────────────────────────────────────────────────────────────────────
        private static void Pf3HeadsAndTrains()
        {
            const string W = "pad/PF-3";
            // A tower on a 26 deg rope (DQ: lean about 6, head relative 20) and on a 16 deg rope (plumb), both draw directions.
            foreach (float grade in new[] { 26f, 16f })
                foreach (bool topFirst in new[] { false, true })
                {
                    string tag = F(grade, "F0") + (topFirst ? " top-first" : " bottom-first");
                    float run = 80f, rise = run * math.tan(math.radians(grade)), h = 14f;
                    float3 low = new float3(0f, 1000f - rise + h, 1000f - run), high = new float3(0f, 1000f + rise + h, 1000f + run);
                    float3 foot = new float3(0f, 1000f, 1000f);
                    var drawn = new List<float3> { topFirst ? high : low, foot, topFirst ? low : high };
                    float3 prev = drawn[0], next = drawn[2];
                    var up = new List<float2>();
                    LiftAlignMath.SectionUphill(drawn, drawn[0].y, drawn[2].y, up);
                    quaternion yaw = LiftAlignMath.PadUphillYaw(VanillaYaw(drawn, 1), up[1]);
                    float3 fwd = math.mul(yaw, Z);
                    Check(fwd.z > 0.99f, W, tag + ": pad not uphill, forward " + fwd);
                    // TowerLeanSystem's pitch and lean: ChordPitchDeg over prev -> next, signed along the turned +z
                    float pitch = LiftAlignMath.ChordPitchDeg(-math.length(prev.xz - foot.xz), prev.y, 0f, foot.y + h, math.length(next.xz - foot.xz),
                        next.y);
                    float pitchAlongZ = math.dot(fwd.xz, next.xz - prev.xz) >= 0f ? pitch : -pitch;
                    Check(pitchAlongZ > 0f, W, tag + ": the rope does not rise toward the pad (" + F(pitchAlongZ, "F2") + ")");
                    float lean = LiftAlignMath.LeanDeg("detachable", pitch);
                    quaternion q = LiftAlignMath.LeanRotation(yaw, pitchAlongZ, lean);
                    float mast = LiftAlignMath.MastPitch(q);
                    Check(math.abs(mast - math.radians(lean)) < 1e-4f, W, tag + ": MastPitch " + F(math.degrees(mast), "F3") + " for lean " + F(lean, "F3"));
                    float3 top = math.mul(q, new float3(0f, 1f, 0f));
                    Check(lean == 0f ? math.abs(top.z) < 1e-5f : top.z < 0f, W, tag + ": the top tips " + top + " (downhill is -z)");
                    // the engine's rope lanes leave the (leaned) anchor toward each neighbor. TrainPitchSystem reads them along +z
                    float3 anchor = foot + math.mul(q, new float3(0f, h, 0f));
                    float3 toNext = anchor + (next - anchor) / 3f, toPrev = anchor + (prev - anchor) / 3f;
                    int sideHigh = LiftAlignMath.LaneEndSide(anchor, topFirst ? toPrev : toNext, fwd);
                    int sideLow = LiftAlignMath.LaneEndSide(anchor, topFirst ? toNext : toPrev, fwd);
                    Check(sideHigh == 1 && sideLow == -1, W, tag + ": the uphill rope feeds side " + sideHigh + ", the downhill " + sideLow);
                    float front = LiftAlignMath.LaneEndPitch(anchor, topFirst ? toPrev : toNext, fwd);
                    float back = LiftAlignMath.LaneEndPitch(anchor, topFirst ? toNext : toPrev, fwd);
                    Check(front > 0f && back > 0f, W, tag + ": half pitches " + F(math.degrees(front), "F2") + " / " + F(math.degrees(back), "F2") +
                          " (both rise toward +z)");
                    LiftAlignMath.HeadedHalfPitches(front - mast, 1, back - mast, 1, LiftAlignMath.HeadRelMax, out float head, out float pf,
                        out float pb);
                    Check(math.abs(head + mast - 0.5f * (front + back)) < 1e-4f, W, tag + ": head " + F(math.degrees(head), "F3") +
                          " + mast is not the rope bisector " + F(math.degrees(0.5f * (front + back)), "F3"));
                    Check(math.abs(pf + mast - front) < 1e-4f && math.abs(pb + mast - back) < 1e-4f, W, tag + ": halves off the rope");
                }
            s_Report.Add("PF-3 a turned DQ tower at 26 / 16 deg, drawn both ways: rope rises toward the pad, front = uphill rope, MastPitch = " +
                         "+lean, head + mast = the rope bisector");
        }
    }
}
