// The lift-line tool's section pick (DECISIONS 591, F1): the closest rope to the cursor ray, within a screen-space tolerance, names the
// The vanilla bulldozer tests a lift edge only on a sheet far above the ropes (EdgeGeometry + the composition's m_SurfaceHeight.max,
// Game.Net/RaycastJobs.cs:437-460). The sheet is 9.9 m above the ropes on a 473 m tram span and about 13 m at 1 km
// (docs/research/tram-bulldoze-hitbox-2026-11-03.md). So this tests what the player sees: the rope lanes' own curves.
// Pure math over plain data (Unity.Mathematics only), so tests/LiftBatch proves it (RopePickTests, RP-1..RP-9). The Game-side harvest,
// the camera ray and the delete live in LiftLineToolSystem.Sections.cs.
// Engine notes: EngineNotes/LiftLineToolSystem.md, "Section delete by the rope".

using System.Collections.Generic;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public static class LiftRopePick
    {
        /// <summary>Tolerance = camera distance x tan(fov / 2) x ScreenFrac: 2.5 % of the half view height. It is about 13 px at 1080p and
        /// the same share of the screen at any resolution. Vanilla's lane pick uses 1 % (Game.Net/RaycastJobs.cs:793, :813-816,
        /// GetMinLaneRadius), about 5 px: too fine for a rope one or two pixels thick.</summary>
        public const float ScreenFrac = 0.025f;

        /// <summary>The tolerance never drops under this near the camera (the tram's track and haul ropes lie 0.5 m apart).</summary>
        public const float MinTolM = 1f;

        /// <summary>Two ropes whose misses (in tolerances) lie within this of the best one: the one nearer the camera wins, as it is drawn
        /// in front.</summary>
        public const float TieFrac = 0.15f;

        /// <summary>Rope polyline: one segment per SampleStepM of chord, MinSegments to MaxSegments. A 1 km tram rope with 40 m of sag
        /// (h 0.04) at 64 segments deviates about S / n^2 = 0.01 m from its curve (RP-8).</summary>
        public const float SampleStepM = 10f;
        public const int MinSegments = 8, MaxSegments = 64;

        /// <summary>One rope of a section as a polyline in a shared point list.</summary>
        public struct Rope
        {
            public int Section;     // the caller's section index
            public int First;       // first point in the shared point list
            public int Count;       // points (2 or more)
            public float3 Min, Max; // bounds of those points
        }

        public struct Hit
        {
            public int Section, Rope;
            public float MissM;     // distance between the ray and the rope
            public float TolM;      // the tolerance at that camera distance
            public float CamM;      // distance from the ray origin to the closest ray point
            public float Score;     // MissM / TolM: 0 dead on, 1 at the tolerance edge
            public float RopeT;     // 0..1 along the rope polyline (by segment index)
            public float3 RopePoint, RayPoint;
        }

        /// <summary>The tolerance (m) at <paramref name="camM"/> from the camera. <paramref name="fovTan"/> = tan(vertical fov /
        /// 2).</summary>
        public static float Tolerance(float camM, float fovTan) => math.max(MinTolM, camM * fovTan * ScreenFrac);

        /// <summary>Polyline segments for a rope whose ends lie <paramref name="chordM"/> apart.</summary>
        public static int Segments(float chordM) => math.clamp((int)math.ceil(chordM / SampleStepM), MinSegments, MaxSegments);

        /// <summary>A cubic Bezier point (the shape of Colossal.Mathematics.Bezier4x3 / MathUtils.Position).</summary>
        public static float3 Bezier(float3 a, float3 b, float3 c, float3 d, float t)
        {
            float u = 1f - t;
            return u * u * u * a + 3f * u * u * t * b + 3f * u * t * t * c + t * t * t * d;
        }

        /// <summary>Appends a rope's polyline (Segments(chord) + 1 points) to <paramref name="pts"/> and returns its Rope entry.</summary>
        public static Rope AddRope(List<float3> pts, int section, float3 a, float3 b, float3 c, float3 d)
        {
            int n = Segments(math.distance(a, d));
            var rope = new Rope { Section = section, First = pts.Count, Count = n + 1, Min = a, Max = a };
            for (int i = 0; i <= n; i++)
            {
                float3 p = Bezier(a, b, c, d, i / (float)n);
                pts.Add(p);
                rope.Min = math.min(rope.Min, p);
                rope.Max = math.max(rope.Max, p);
            }
            return rope;
        }

        /// <summary>Distance between segment [p, q] and the ray segment [o, o + u x len] (u unit). s in [0, 1] on [p, q], t in [0, len] on
        /// the ray. Ericson, Real-Time Collision Detection 5.1.9, with the ray kept unit-length for float precision over
        /// kilometers.</summary>
        public static float SegmentRay(float3 p, float3 q, float3 o, float3 u, float len, out float s, out float t)
        {
            float3 d1 = q - p, r = p - o;
            float a = math.dot(d1, d1), b = math.dot(d1, u), c = math.dot(d1, r), f = math.dot(u, r);
            if (a <= 1e-8f)
            {
                s = 0f;
                t = math.clamp(f, 0f, len);
            }
            else
            {
                float denom = a - b * b;
                s = denom > 1e-8f ? math.saturate((b * f - c) / denom) : 0f;
                t = b * s + f;
                if (t < 0f)
                {
                    t = 0f;
                    s = math.saturate(-c / a);
                }
                else if (t > len)
                {
                    t = len;
                    s = math.saturate((b * len - c) / a);
                }
            }
            return math.distance(p + d1 * s, o + u * t);
        }

        /// <summary>True when the ray segment [o, o + u x len] meets the box [min, max] (slab test).</summary>
        public static bool RayHitsBox(float3 o, float3 u, float len, float3 min, float3 max)
        {
            float t0 = 0f, t1 = len;
            for (int k = 0; k < 3; k++)
            {
                float uk = u[k], ok = o[k];
                if (math.abs(uk) < 1e-9f)
                {
                    if (ok < min[k] || ok > max[k])
                        return false;
                    continue;
                }
                float inv = 1f / uk;
                float ta = (min[k] - ok) * inv, tb = (max[k] - ok) * inv;
                t0 = math.max(t0, math.min(ta, tb));
                t1 = math.min(t1, math.max(ta, tb));
                if (t0 > t1)
                    return false;
            }
            return true;
        }

        /// <summary>The section under the cursor ray [<paramref name="rayA"/>, <paramref name="rayB"/>]. For each rope of an enabled
        /// section (<paramref name="enabled"/>[Section], null = all), the method takes the polyline point closest to the ray. A rope counts
        /// when that miss is within Tolerance(camera distance) and it lies no farther than <paramref name="maxCamM"/> (+ its tolerance)
        /// along the ray (the terrain hit: a rope behind a hill is not seen). The best score (miss / tolerance) wins. Ropes within TieFrac
        /// of it go to the one nearest the camera. Each rope's box, grown by the tolerance at its farthest corner, culls it first.
        /// <paramref name="scratch"/> holds the candidates (cleared here, no allocation once grown). Returns false when no rope is
        /// near.</summary>
        public static bool Pick(IReadOnlyList<float3> pts, IReadOnlyList<Rope> ropes, IReadOnlyList<bool> enabled, float3 rayA, float3 rayB,
            float fovTan, float maxCamM, List<Hit> scratch, out Hit hit, out int segmentsTested)
        {
            hit = default;
            segmentsTested = 0;
            scratch.Clear();
            float len = math.distance(rayA, rayB);
            if (!(len > 1e-3f) || ropes == null || pts == null)
                return false;
            float3 u = (rayB - rayA) / len;
            float best = float.PositiveInfinity;
            for (int r = 0; r < ropes.Count; r++)
            {
                Rope rope = ropes[r];
                if (rope.Count < 2 || (enabled != null && (rope.Section < 0 || rope.Section >= enabled.Count || !enabled[rope.Section])))
                    continue;
                float3 far = math.max(math.abs(rope.Min - rayA), math.abs(rope.Max - rayA));
                float grow = Tolerance(math.length(far), fovTan);
                if (!RayHitsBox(rayA, u, len, rope.Min - grow, rope.Max + grow))
                    continue;
                var h = new Hit { Section = rope.Section, Rope = r, Score = float.PositiveInfinity };
                for (int i = 0; i < rope.Count - 1; i++)
                {
                    float3 p = pts[rope.First + i], q = pts[rope.First + i + 1];
                    segmentsTested++;
                    float miss = SegmentRay(p, q, rayA, u, len, out float s, out float t);
                    float tol = Tolerance(t, fovTan);
                    float score = miss / tol;
                    if (score >= h.Score)
                        continue;
                    h.Score = score;
                    h.MissM = miss;
                    h.TolM = tol;
                    h.CamM = t;
                    h.RopeT = (i + s) / (rope.Count - 1);
                    h.RopePoint = math.lerp(p, q, s);
                    h.RayPoint = rayA + u * t;
                }
                if (h.Score > 1f || h.CamM > maxCamM + h.TolM)
                    continue;
                scratch.Add(h);
                best = math.min(best, h.Score);
            }
            if (scratch.Count == 0)
                return false;
            bool found = false;
            for (int i = 0; i < scratch.Count; i++)
            {
                Hit h = scratch[i];
                if (h.Score > best + TieFrac)
                    continue;
                if (!found || h.CamM < hit.CamM)
                {
                    hit = h;
                    found = true;
                }
            }
            return found;
        }
    }
}
