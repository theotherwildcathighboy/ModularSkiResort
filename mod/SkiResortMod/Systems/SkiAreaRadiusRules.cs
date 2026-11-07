// Pure rules of the ski area radius (the tether: the nearest Welcome Sign within this distance owns a run, lift, lodge or school) and of
// the site survey the snow-line tooltip shows while a sign is being placed.
//   ClampKm       the option's value (km) gives a legal radius: [MinKm, MaxKm] on StepKm. NaN / infinity gives DefaultKm.
//   RingPoint     point i of n on the circle (the ring overlay draws n chords, and RingSegments keeps a 10 km ring's chord sag under 2 m).
//   Survey        min / max dry ground, vertical, the skiable vertical (from the snow line, or the lowest ground when that is higher, up to
//                 the highest ground), and the share of the disc above the snow line and the guns line. The method is a square grid of
//                 SurveySide x SurveySide cell centers over the disc's bounding box. A cell center is kept when inside the disc AND inside
//                 the playable bounds AND dry (height sampler returns NaN for water or no data). Cell = 2R / SurveySide, so the cost is the
//                 same at every radius (~4,070 samples in a full disc).
// Pure file: System only (tests/ResortBatch links it).
// Engine notes: EngineNotes/SkiAreaRadius.md

using System;

namespace SkiResortMod.Systems
{
    public static class SkiAreaRadiusRules
    {
        public const float DefaultKm = 5f;
        public const float MinKm = 2f;
        public const float MaxKm = 10f;
        public const float StepKm = 0.5f;

        public const int RingSegments = 160;       // chord sag at 10 km: R (1 - cos(pi / 160)) = 1.9 m
        public const int SurveySide = 72;          // cells across the diameter
        public const float SurveyRebuildMoveM = 50f; // re-survey when the center moved farther than this

        /// <summary>The legal radius in km for an option value: clamped to [MinKm, MaxKm] and rounded to the nearest StepKm. A value that
        /// is not finite gives DefaultKm.</summary>
        public static float ClampKm(float km)
        {
            if (float.IsNaN(km) || float.IsInfinity(km))
                return DefaultKm;
            if (km < MinKm)
                km = MinKm;
            if (km > MaxKm)
                km = MaxKm;
            return (float)(Math.Round(km / StepKm, MidpointRounding.AwayFromZero) * StepKm);
        }

        /// <summary>The radius in meters for an option value (ClampKm x 1000).</summary>
        public static float MetersOf(float km) => ClampKm(km) * 1000f;

        /// <summary>Point <paramref name="i"/> of <paramref name="n"/> on the circle around (cx, cz). i = n closes the ring at i = 0.</summary>
        public static void RingPoint(float cx, float cz, float radiusM, int i, int n, out float x, out float z)
        {
            double a = 2.0 * Math.PI * (i % n) / n;
            x = cx + (float)(radiusM * Math.Cos(a));
            z = cz + (float)(radiusM * Math.Sin(a));
        }

        public struct SurveyResult
        {
            /// <summary>Dry samples kept (0 = nothing to show: all water, off the map, or no terrain).</summary>
            public int Samples;
            /// <summary>Cell centers inside the disc and the playable bounds, wet or dry.</summary>
            public int InDisc;
            public float MinM, MaxM;
            public int AboveSnow, AboveGuns;

            /// <summary>The vertical above the snow line: MaxM - max(MinM, snow line), at least 0. It is 0 with no samples or a NaN snow
            /// line.</summary>
            public float SkiableVerticalM;

            public float VerticalM => Samples > 0 ? MaxM - MinM : 0f;

            /// <summary>Share of the dry samples at or above the snow line, 0-100 rounded to an integer.</summary>
            public int PctAboveSnow => Samples > 0 ? (int)Math.Round(100.0 * AboveSnow / Samples, MidpointRounding.AwayFromZero) : 0;

            public int PctAboveGuns => Samples > 0 ? (int)Math.Round(100.0 * AboveGuns / Samples, MidpointRounding.AwayFromZero) : 0;
        }

        /// <summary>Surveys the disc of <paramref name="radiusM"/> around (cx, cz), clipped to the playable bounds
        /// [minX, maxX] x [minZ, maxZ]. <paramref name="dryHeightAt"/>(x, z) returns the ground height, or NaN for water / no data.
        /// A NaN snow or guns line counts nothing above it.</summary>
        public static SurveyResult Survey(Func<float, float, float> dryHeightAt, float cx, float cz, float radiusM,
            float minX, float minZ, float maxX, float maxZ, float snowLineM, float gunsLineM)
        {
            var r = new SurveyResult { MinM = float.NaN, MaxM = float.NaN };
            if (dryHeightAt == null || !(radiusM > 0f) || float.IsNaN(cx) || float.IsNaN(cz))
                return r;
            int side = SurveySide;
            float cell = 2f * radiusM / side;
            float x0 = cx - radiusM + 0.5f * cell, z0 = cz - radiusM + 0.5f * cell;
            float r2 = radiusM * radiusM;
            float lo = float.MaxValue, hi = float.MinValue;
            for (int j = 0; j < side; j++)
            {
                float z = z0 + j * cell;
                if (z < minZ || z > maxZ)
                    continue;
                float dz = z - cz;
                for (int i = 0; i < side; i++)
                {
                    float x = x0 + i * cell;
                    if (x < minX || x > maxX)
                        continue;
                    float dx = x - cx;
                    if (dx * dx + dz * dz > r2)
                        continue;
                    r.InDisc++;
                    float h = dryHeightAt(x, z);
                    if (float.IsNaN(h) || float.IsInfinity(h))
                        continue;
                    r.Samples++;
                    if (h < lo)
                        lo = h;
                    if (h > hi)
                        hi = h;
                    if (h >= snowLineM)
                        r.AboveSnow++;
                    if (h >= gunsLineM)
                        r.AboveGuns++;
                }
            }
            if (r.Samples > 0)
            {
                r.MinM = lo;
                r.MaxM = hi;
                if (!float.IsNaN(snowLineM))
                    r.SkiableVerticalM = Math.Max(0f, hi - Math.Max(lo, snowLineM));
            }
            return r;
        }
    }
}
