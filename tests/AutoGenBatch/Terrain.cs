// Synthetic terrains for AutoGenBatch: an analytic height function sampled into a HeightSnapshot exactly as the game stores heights
// (R16 texels on the vanilla 4096 x 14,336 m map, 3.5 m texels, heightScale 4096 m), region polygons, and value-noise fBm.

using System;
using SkiResortMod.Systems;
using Unity.Mathematics;

namespace AutoGenBatch
{
    internal delegate double HeightFn(double x, double z);

    internal static class Terrain
    {
        // The vanilla default map: TerrainSystem.cs:2618-2622 (scale = (res.x, res.y - 1, res.z) / (14336, heightScale, 14336);
        // texel 0's center at -offset.xz = -7166.25, the capture origin of tests/SlopeBatch/captures).
        public const int Res = 4096;
        public static readonly float3 Scale = new float3(4096f / 14336f, 65535f / 4096f, 4096f / 14336f);
        public static readonly float3 Offset = new float3(7166.25f, 0f, 7166.25f);

        /// <summary>Snapshot of the rectangle SkiRunAutoPlanner.SnapshotRect asks for, heights from f (clamped to the R16 range).</summary>
        public static HeightSnapshot Snapshot(HeightFn f, float2[] region, Func<double, double, bool> wet = null)
        {
            SkiRunAutoPlanner.SnapshotRect(region, Res, Res, Scale, Offset, out int x0, out int z0, out int w, out int d);
            HeightSnapshot s = HeightSnapshot.Allocate(x0, z0, w, d, Res, Res, Scale, Offset, wet != null);
            for (int l = 0; l < d; l++)
            {
                double z = -Offset.z + (z0 + l) / (double)Scale.z;
                for (int k = 0; k < w; k++)
                {
                    double x = -Offset.x + (x0 + k) / (double)Scale.x;
                    double raw = Math.Round(f(x, z) * Scale.y);
                    s.Heights[l * w + k] = (ushort)Math.Max(0.0, Math.Min(65535.0, raw));
                    if (wet != null && wet(x, z))
                        s.Wet[l * w + k] = 1;
                }
            }
            return s;
        }

        public static float2[] Rect(float x0, float z0, float x1, float z1) =>
            new[] { new float2(x0, z0), new float2(x1, z0), new float2(x1, z1), new float2(x0, z1) };

        public static float2[] Square(float cx, float cz, float half) => Rect(cx - half, cz - half, cx + half, cz + half);

        public static float2[] Disc(float cx, float cz, float r, int n)
        {
            var p = new float2[n];
            for (int i = 0; i < n; i++)
            {
                double a = 2.0 * Math.PI * i / n;
                p[i] = new float2(cx + (float)(r * Math.Cos(a)), cz + (float)(r * Math.Sin(a)));
            }
            return p;
        }

        /// <summary>Plane falling toward +z at the given grade.</summary>
        public static HeightFn Plane(double grade) => (x, z) => 1500.0 - grade * z;

        /// <summary>Seeded value-noise fBm on a tilted mountainside (the "noise" fixtures).</summary>
        public static HeightFn Fbm(uint seed, double tilt, double amp)
        {
            return (x, z) =>
            {
                double h = 1800.0 - tilt * z - 0.08 * Math.Abs(x);
                double a = amp, wl = 700.0;
                for (int o = 0; o < 5; o++)
                {
                    h += a * Value(seed + (uint)o * 7919u, x / wl, z / wl);
                    a *= 0.5;
                    wl *= 0.5;
                }
                return h;
            };
        }

        // ---------------------------------------------------------------- the field mirror (field test 2026-10-26b)
        // An alpine bowl falling toward +z: rim near z = -1100, base near z = +1100, 2.2 km of horizontal and about 1000 m of vertical
        // over the region. Fall-line coordinate s = z - x^2 / BowlR curves the contours into a bowl (the walls rise toward the sides).
        // Along s the grade is 60 % on the upper fifth (rocky: two patchy cliff bands, summit faces over 100 %), 36 % in the middle,
        // 12 % to 7 % below; fBm relief stretched along the fall line makes ridges and gullies (about 60 m peak at 520 m wavelength in the
        // middle, less above and below), a few drainage gullies cut the gentle lower third, and 2-3 summits sit on the rim at least
        // 400 m apart. Value noise with smoothstep weights: C1 everywhere, deterministic per seed.

        private const double AlpineRimS = -1100.0, AlpineSpanS = 2200.0, AlpineRimY = 1750.0, BowlR = 5000.0;
        private const double ProfileStep = 2.0, ProfileS0 = -1600.0, ProfileS1 = 1700.0;

        /// <summary>Grade along the fall line at t = (s - rim) / span (0 rim, 1 base); negative behind the rim (the crest).</summary>
        private static double AlpineGrade(double t)
        {
            if (t < 0.0)
                return Lerp(0.60, -0.30, Smooth(0.0, -0.06, t));
            if (t < 0.30)
                return Lerp(0.60, 0.36, Smooth(0.18, 0.30, t));
            if (t < 0.72)
                return Lerp(0.36, 0.12, Smooth(0.58, 0.72, t));
            return Lerp(0.12, 0.07, Smooth(0.72, 1.10, t));
        }

        /// <summary>Height of the bare profile (no relief) at fall-line coordinate s: AlpineRimY at the rim, integrated grade elsewhere.</summary>
        private static double[] AlpineProfile()
        {
            int n = (int)((ProfileS1 - ProfileS0) / ProfileStep) + 1;
            var h = new double[n];
            for (int k = 1; k < n; k++)
            {
                double sm = ProfileS0 + (k - 0.5) * ProfileStep;
                h[k] = h[k - 1] - ProfileStep * AlpineGrade((sm - AlpineRimS) / AlpineSpanS);
            }
            int rim = (int)Math.Round((AlpineRimS - ProfileS0) / ProfileStep);
            double off = AlpineRimY - h[rim];
            for (int k = 0; k < n; k++)
                h[k] += off;
            return h;
        }

        /// <summary>The field-mirror mountain (see the block comment above); seed moves the summits, the relief, the cliff bands and
        /// the gullies.</summary>
        public static HeightFn Alpine(uint seed)
        {
            double[] prof = AlpineProfile();
            var rng = new Unity.Mathematics.Random(seed * 2654435761u | 1u);
            int summits = seed % 3 == 0 ? 2 : 3;
            double[] sx = summits == 3 ? new[] { -800.0, -40.0, 720.0 } : new[] { -560.0, 480.0 };
            var px = new double[summits];
            var pz = new double[summits];
            var pa = new double[summits];
            var ps = new double[summits];
            for (int k = 0; k < summits; k++)
            {
                px[k] = sx[k] + (rng.NextFloat() - 0.5) * 160.0;          // >= 400 m apart after the jitter
                pz[k] = AlpineRimS + 30.0 + rng.NextFloat() * 50.0 + px[k] * px[k] / BowlR;
                pa[k] = 100.0 + rng.NextFloat() * 50.0;
                ps[k] = 210.0 + rng.NextFloat() * 50.0;
            }
            var gx = new double[3];
            for (int k = 0; k < 3; k++)
                gx[k] = -650.0 + 650.0 * k + (rng.NextFloat() - 0.5) * 300.0;
            uint sRelief = seed * 101u + 7u, sCliff = seed * 131u + 11u, sGully = seed * 151u + 13u;
            return (x, z) =>
            {
                double s = z - x * x / BowlR;
                double t = (s - AlpineRimS) / AlpineSpanS;
                double u = (s - ProfileS0) / ProfileStep;
                int k0 = Math.Max(0, Math.Min(prof.Length - 2, (int)Math.Floor(u)));
                double f = Math.Max(0.0, Math.Min(1.0, u - k0));
                double h = prof[k0] + (prof[k0 + 1] - prof[k0]) * f;

                // Ridges and gullies: fBm stretched 2x along the fall line, strongest in the middle third
                double env = t < 0.25 ? Lerp(0.45, 1.0, Smooth(0.05, 0.25, t)) : Lerp(1.0, 0.25, Smooth(0.55, 0.75, t));
                double a = 60.0, wl = 520.0, rel = 0.0;
                for (int o = 0; o < 5; o++)
                {
                    rel += a * Value(sRelief + (uint)o * 7919u, x / wl, s / (2.0 * wl));
                    a *= 0.5;
                    wl *= 0.5;
                }
                h += env * rel;

                // Fine roughness everywhere (rolling ground below, rock above)
                h += (t < 0.3 ? 2.5 : 1.5) * Value(sRelief + 997u, x / 45.0, z / 45.0);

                // Summits on the rim
                for (int k = 0; k < px.Length; k++)
                {
                    double dx = x - px[k], dz = z - pz[k];
                    h += pa[k] * Math.Exp(-(dx * dx + dz * dz) / (2.0 * ps[k] * ps[k]));
                }

                // Two patchy rock bands in the upper fifth: a 30 m tanh step over ~20 m where the mask is on (cliff cells over 100 %)
                for (int b = 0; b < 2; b++)
                {
                    double tc = (b == 0 ? 0.07 : 0.15) + 0.03 * Value(sCliff + (uint)b, x / 400.0, 0.5);
                    double mask = Math.Max(0.0, Math.Min(1.0, 2.5 * Value(sCliff + 17u + (uint)b, x / 260.0, 3.5) + 0.3));
                    if (mask > 0.0)
                        h += mask * 15.0 * Math.Tanh((AlpineRimS + tc * AlpineSpanS - s) / 10.0);
                }

                // Drainage gullies in the lower third (8 m deep, meandering)
                double ramp = Smooth(0.55, 0.75, t);
                if (ramp > 0.0)
                    for (int k = 0; k < gx.Length; k++)
                    {
                        double cx = gx[k] + 60.0 * Value(sGully + (uint)k, s / 300.0, 0.5);
                        double d = (x - cx) / 40.0;
                        h -= ramp * 8.0 * Math.Exp(-d * d);
                    }
                return h;
            };
        }

        /// <summary>Field-mirror regions on Alpine: the 4-corner ~3.9 km2 region over most of the bowl (summits to base), or (lower) a
        /// ~1.1 km2 region on the lower-middle slope.</summary>
        public static float2[] AlpineRegion(bool lower = false) => lower
            ? new[] { new float2(-480f, -20f), new float2(530f, -60f), new float2(510f, 1020f), new float2(-500f, 1000f) }
            : new[] { new float2(-1020f, -1010f), new float2(990f, -1040f), new float2(1010f, 960f), new float2(-980f, 990f) };

        private static double Smooth(double e0, double e1, double v)
        {
            double t = Math.Max(0.0, Math.Min(1.0, (v - e0) / (e1 - e0)));
            return t * t * (3.0 - 2.0 * t);
        }

        private static double Lerp(double a, double b, double t) => a + (b - a) * t;

        private static double Value(uint seed, double x, double z)
        {
            double fx = Math.Floor(x), fz = Math.Floor(z);
            int ix = (int)fx, iz = (int)fz;
            double tx = x - fx, tz = z - fz;
            tx = tx * tx * (3 - 2 * tx);
            tz = tz * tz * (3 - 2 * tz);
            double a = Hash(seed, ix, iz), b = Hash(seed, ix + 1, iz), c = Hash(seed, ix, iz + 1), d = Hash(seed, ix + 1, iz + 1);
            return (a + (b - a) * tx) + ((c + (d - c) * tx) - (a + (b - a) * tx)) * tz;
        }

        private static double Hash(uint seed, int x, int z)
        {
            uint h = seed * 0x9E3779B1u ^ (uint)x * 0x85EBCA77u ^ (uint)z * 0xC2B2AE3Du;
            h ^= h >> 15;
            h *= 0x2C1B3C6Du;
            h ^= h >> 12;
            h *= 0x297A2D39u;
            h ^= h >> 15;
            return h / (double)uint.MaxValue * 2.0 - 1.0;
        }
    }
}
