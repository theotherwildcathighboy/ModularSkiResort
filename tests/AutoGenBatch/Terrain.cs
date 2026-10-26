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
