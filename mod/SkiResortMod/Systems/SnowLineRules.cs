// SnowLineRules — the pure rules of the winter snow-line overlay (ticket T32, 2026-10-16;
// docs/research/field/2026-10-15/build/T32-snowline.md). No engine types: System only, so a host can link it.
//
//   WinterWindow   the climate's winter as a span of the normalized year: the SeasonInfo named
//                  "SeasonWinter" starts at its m_StartTime and ends where the next season starts
//                  (vanilla 0.9167 → 1.1667 = December-February, ClimatePrefab.m_Seasons; snow-climate.md §3).
//                  Fallback when no season carries that name: December-February by the calendar.
//   MeanOverWindow the arithmetic mean of hourly climate samples across the window (the curve is authored in
//                  24 keys per game day, ClimatePrefab.RebuildTemperatureCurves: 288 keys over 12 days).
//   ElevationAt    the lapse formula the game itself uses (SnowSystem.SnowTransfer, snow-climate.md §1.2):
//                      T_local(h) = T − (h − temperatureBaseHeight) × lapse
//                  solved for T_local(h) = threshold:  h = base + (T − threshold) / lapse.
//   MarchingSquares  the contour of a height grid at one level, as straight segments (linear interpolation
//                  along the cell edges; the two ambiguous saddle cases are resolved by the cell's mean).
//
// Everything is allocation-free after the caller sized its arrays.

using System;

namespace SkiResortMod.Systems
{
    internal static class SnowLineRules
    {
        // ── the grid (bounded cost: GridSide² ≤ 10,000 height samples per rebuild) ──────────────────────
        public const int GridSide = 96;                 // 96 × 96 = 9,216 samples
        public const float CellM = 40f;                 // 3.8 km square around the camera pivot
        public const float RebuildMoveM = 100f;         // rebuild when the pivot moved farther than this (plan view)
        public const int MaxSegmentsPerContour = 1536;  // a 95 × 95 cell grid rarely makes a third of this

        public const float NoSnowLine = float.NaN;

        /// <summary>The fallback calendar window: December to February = [11/12, 14/12) of the year.</summary>
        public const float CalendarWinterStart = 11f / 12f;
        public const float CalendarWinterEnd = 14f / 12f;

        /// <summary>One contour segment in world X/Z.</summary>
        public struct Segment
        {
            public float AX, AZ, BX, BZ;
        }

        /// <summary>Span of the winter season in normalized year time: <paramref name="start"/> in [0, 1),
        /// <paramref name="end"/> in (start, start + 1]. <paramref name="startTimes"/> are every season's
        /// m_StartTime; <paramref name="winterIndex"/> picks the winter one (-1 → calendar December-February).
        /// The end is the nearest later start (wrapping the year), or start + 1 for a single season.</summary>
        public static void WinterWindow(float[] startTimes, int winterIndex, out float start, out float end)
        {
            if (startTimes == null || winterIndex < 0 || winterIndex >= startTimes.Length)
            {
                start = CalendarWinterStart;
                end = CalendarWinterEnd;
                return;
            }
            start = Wrap01(startTimes[winterIndex]);
            float best = float.MaxValue;
            for (int i = 0; i < startTimes.Length; i++)
            {
                if (i == winterIndex)
                    continue;
                float d = Wrap01(startTimes[i]) - start;
                if (d <= 0f)
                    d += 1f;
                if (d < best)
                    best = d;
            }
            end = best == float.MaxValue || best > 1f ? start + 1f : start + best;
        }

        /// <summary>Mean, minimum and maximum of <paramref name="temperatureAt"/> (normalized year date → °C) over
        /// the window, sampled hourly at the middle of each hour: 24 × daysPerYear × span samples (72 for the
        /// vanilla three-month winter). A non-positive span or no samples return NaN.</summary>
        public static float MeanOverWindow(Func<float, float> temperatureAt, float start, float end, int daysPerYear,
            out float min, out float max, out int samples)
        {
            min = float.NaN;
            max = float.NaN;
            samples = 0;
            int days = daysPerYear < 1 ? 1 : daysPerYear;
            float span = end - start;
            if (!(span > 0f))
                return float.NaN;
            int n = (int)Math.Round(24.0 * days * span);
            if (n < 1)
                n = 1;
            double sum = 0.0;
            float lo = float.MaxValue, hi = float.MinValue;
            for (int k = 0; k < n; k++)
            {
                double t = start + (k + 0.5) / n * span;
                t -= Math.Floor(t);
                float v = temperatureAt((float)t);
                sum += v;
                if (v < lo) lo = v;
                if (v > hi) hi = v;
            }
            min = lo;
            max = hi;
            samples = n;
            return (float)(sum / n);
        }

        /// <summary>Elevation (world metres) where the local temperature equals <paramref name="thresholdC"/> for a
        /// sea-level (reference-elevation) temperature <paramref name="meanC"/>: base + (T − threshold) / lapse.
        /// NaN when the lapse is not positive or the temperature is not finite (no usable line).</summary>
        public static float ElevationAt(float baseHeight, float meanC, float thresholdC, float lapsePerM)
        {
            if (!(lapsePerM > 0f) || float.IsNaN(meanC) || float.IsInfinity(meanC))
                return NoSnowLine;
            return baseHeight + (meanC - thresholdC) / lapsePerM;
        }

        /// <summary>The same formula read the other way: local °C at an elevation (for the tests).</summary>
        public static float LocalTemperature(float baseHeight, float meanC, float elevation, float lapsePerM)
        {
            return meanC - (elevation - baseHeight) * lapsePerM;
        }

        /// <summary>Grid origin that keeps the lattice world-anchored (a multiple of the cell size), so the contour
        /// does not shimmer as the pivot moves. The grid is centred on <paramref name="centre"/>.</summary>
        public static float Origin(float centre, int side, float cell)
        {
            return (float)(Math.Floor((centre - side * 0.5f * cell) / cell) * cell);
        }

        /// <summary>Marching squares over a side × side height grid (row-major, index j * side + i; world position
        /// (originX + i × cell, originZ + j × cell)). Writes straight segments for the level into
        /// <paramref name="output"/> and returns the count; stops at its length. A corner counts as above when its
        /// height is ≥ level.</summary>
        public static int MarchingSquares(float[] heights, int side, float originX, float originZ, float cell,
            float level, Segment[] output)
        {
            if (heights == null || output == null || side < 2 || heights.Length < side * side || float.IsNaN(level))
                return 0;
            int count = 0;
            for (int j = 0; j < side - 1; j++)
            {
                for (int i = 0; i < side - 1; i++)
                {
                    float h00 = heights[j * side + i];
                    float h10 = heights[j * side + i + 1];
                    float h11 = heights[(j + 1) * side + i + 1];
                    float h01 = heights[(j + 1) * side + i];
                    int mask = (h00 >= level ? 1 : 0) | (h10 >= level ? 2 : 0) | (h11 >= level ? 4 : 0) | (h01 >= level ? 8 : 0);
                    if (mask == 0 || mask == 15)
                        continue;
                    float x0 = originX + i * cell, z0 = originZ + j * cell;
                    float x1 = x0 + cell, z1 = z0 + cell;
                    // Edge crossings: e0 bottom (00-10), e1 right (10-11), e2 top (01-11), e3 left (00-01).
                    switch (mask)
                    {
                        case 1: case 14: Add(output, ref count, E0(x0, z0, cell, h00, h10, level), E3(x0, z0, cell, h00, h01, level)); break;
                        case 2: case 13: Add(output, ref count, E0(x0, z0, cell, h00, h10, level), E1(x1, z0, cell, h10, h11, level)); break;
                        case 3: case 12: Add(output, ref count, E3(x0, z0, cell, h00, h01, level), E1(x1, z0, cell, h10, h11, level)); break;
                        case 4: case 11: Add(output, ref count, E1(x1, z0, cell, h10, h11, level), E2(x0, z1, cell, h01, h11, level)); break;
                        case 6: case 9: Add(output, ref count, E0(x0, z0, cell, h00, h10, level), E2(x0, z1, cell, h01, h11, level)); break;
                        case 7: case 8: Add(output, ref count, E3(x0, z0, cell, h00, h01, level), E2(x0, z1, cell, h01, h11, level)); break;
                        case 5:    // 00 and 11 above
                        case 10:   // 10 and 01 above
                            {
                                bool centreAbove = (h00 + h10 + h11 + h01) * 0.25f >= level;
                                // 5 with the centre above (or 10 with it below): the BELOW corners 10 and 01 are cut off.
                                bool cutBelowCorners = (mask == 5) == centreAbove;
                                if (cutBelowCorners)
                                {
                                    Add(output, ref count, E0(x0, z0, cell, h00, h10, level), E1(x1, z0, cell, h10, h11, level));
                                    Add(output, ref count, E3(x0, z0, cell, h00, h01, level), E2(x0, z1, cell, h01, h11, level));
                                }
                                else
                                {
                                    Add(output, ref count, E0(x0, z0, cell, h00, h10, level), E3(x0, z0, cell, h00, h01, level));
                                    Add(output, ref count, E1(x1, z0, cell, h10, h11, level), E2(x0, z1, cell, h01, h11, level));
                                }
                                break;
                            }
                    }
                    if (count >= output.Length)
                        return output.Length;
                }
            }
            return count;
        }

        // Edge points: (x, z) of the level crossing.
        private static (float x, float z) E0(float x0, float z0, float cell, float ha, float hb, float level)
            => (x0 + cell * Frac(ha, hb, level), z0);
        private static (float x, float z) E1(float x1, float z0, float cell, float ha, float hb, float level)
            => (x1, z0 + cell * Frac(ha, hb, level));
        private static (float x, float z) E2(float x0, float z1, float cell, float ha, float hb, float level)
            => (x0 + cell * Frac(ha, hb, level), z1);
        private static (float x, float z) E3(float x0, float z0, float cell, float ha, float hb, float level)
            => (x0, z0 + cell * Frac(ha, hb, level));

        private static float Frac(float ha, float hb, float level)
        {
            float d = hb - ha;
            if (d == 0f)
                return 0.5f;
            float t = (level - ha) / d;
            return t < 0f ? 0f : t > 1f ? 1f : t;
        }

        private static void Add(Segment[] output, ref int count, (float x, float z) a, (float x, float z) b)
        {
            if (count >= output.Length)
                return;
            output[count++] = new Segment { AX = a.x, AZ = a.z, BX = b.x, BZ = b.z };
        }

        private static float Wrap01(float t)
        {
            t -= (float)Math.Floor(t);
            return t;
        }
    }
}
