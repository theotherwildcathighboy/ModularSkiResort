// Quick runs: the height snapshot that the planner and its rater read. A plain copy of one texel rectangle of the game's CPU heights
// (TerrainHeightData.heights, R16) plus the heightmap's scale / offset / resolution. So a scan reads the terrain once, instead of calling
// TerrainUtils.SampleHeight per texel from the main thread (128 ms per 28,710 texels measured, cattrack-merge-2026-10-24.md 1.4).
// Sample reproduces TerrainUtils.SampleHeightInternal (Game.Simulation/TerrainUtils.cs:121-137) operation for operation: heightmap
// space (world + offset) * scale, floor, clamp to the full resolution, saturate, lerp along x then z, height / scale.y - offset.y.
// So SkiRunRater.Rate over a SnapshotSampler gives the numbers the catalog computes after build with TerrainHeightSampler (same float
// ops). CoreCLR vs Mono bit equality and the in-game parity are phase-2 probe items, UNVERIFIED. The backdrop branch of SampleHeight
// is never reached: the planner masks every cell within OffMapMarginTexels of the heightmap edge.
// Pure file: System and Unity.Mathematics only (the tests/AutoGenBatch rule). Engine notes: EngineNotes/SkiRunAutoPlanner.md

using System;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    /// <summary>One texel rectangle of the CPU heightmap. Texel (k, l) of the full map (0 &lt;= k &lt; ResX, 0 &lt;= l &lt; ResZ) is
    /// Heights[(l - Z0) · Width + (k - X0)] when inside the rectangle. Scale / Offset are TerrainHeightData.scale / offset as fetched
    /// (TerrainSystem.cs:2618-2622). Wet (optional, same indexing) = 1 where the water is deeper than the tool's wet threshold.</summary>
    public sealed class HeightSnapshot
    {
        public ushort[] Heights;
        public byte[] Wet;
        public int X0, Z0, Width, Depth;
        public int ResX, ResZ;
        public float3 Scale, Offset;

        /// <summary>World meters per texel (1 / scale.x, the rater's texel expression, SkiRunCatalogSystem.cs:1373).</summary>
        public float TexelM => 1f / Scale.x;

        /// <summary>World position of texel (0, 0)'s center: the rater's lattice origin (SkiRunCatalogSystem.cs:1374).</summary>
        public double2 Origin => new double2(-Offset.x, -Offset.z);

        /// <summary>Usable: buffers sized to the rectangle, a positive scale, the rectangle inside the map.</summary>
        public bool Valid =>
            Heights != null && Width > 0 && Depth > 0 && (long)Width * Depth <= Heights.Length &&
            (Wet == null || (long)Width * Depth <= Wet.Length) &&
            Scale.x > 0f && Scale.y > 0f && Scale.z > 0f && ResX > 1 && ResZ > 1 &&
            X0 >= 0 && Z0 >= 0 && X0 + Width <= ResX && Z0 + Depth <= ResZ;

        /// <summary>Allocates an empty snapshot of the given rectangle (the tool copies hd.heights rows into Heights).</summary>
        public static HeightSnapshot Allocate(int x0, int z0, int width, int depth, int resX, int resZ, float3 scale, float3 offset,
            bool wet)
        {
            var s = new HeightSnapshot
            {
                X0 = x0, Z0 = z0, Width = Math.Max(0, width), Depth = Math.Max(0, depth), ResX = resX, ResZ = resZ,
                Scale = scale, Offset = offset,
            };
            s.Heights = new ushort[s.Width * s.Depth];
            if (wet)
                s.Wet = new byte[s.Width * s.Depth];
            return s;
        }

        /// <summary>True when every full-map texel in [k0, k1] x [l0, l1] (after clamping to the map) lies in the rectangle.</summary>
        public bool CoversClamped(long k0, long l0, long k1, long l1)
        {
            long ck0 = Clamp(k0, 0, ResX - 1), ck1 = Clamp(k1, 0, ResX - 1);
            long cl0 = Clamp(l0, 0, ResZ - 1), cl1 = Clamp(l1, 0, ResZ - 1);
            return ck0 >= X0 && ck1 < X0 + Width && cl0 >= Z0 && cl1 < Z0 + Depth;
        }

        /// <summary>Raw R16 value of full-map texel (k, l), clamped to the map like SampleHeightInternal. Returns 0 and false outside the
        /// rectangle.</summary>
        public bool TryRaw(long k, long l, out ushort raw)
        {
            k = Clamp(k, 0, ResX - 1);
            l = Clamp(l, 0, ResZ - 1);
            long x = k - X0, z = l - Z0;
            if (x < 0 || z < 0 || x >= Width || z >= Depth)
            {
                raw = 0;
                return false;
            }
            raw = Heights[z * Width + x];
            return true;
        }

        /// <summary>World height of a raw value (TerrainUtils.ToWorldSpace).</summary>
        public float ToWorld(float raw) => raw / Scale.y - Offset.y;

        /// <summary>Wet flag of full-map texel (k, l) (clamped). It is false without a Wet layer or outside the rectangle.</summary>
        public bool IsWet(long k, long l)
        {
            if (Wet == null)
                return false;
            k = Clamp(k, 0, ResX - 1);
            l = Clamp(l, 0, ResZ - 1);
            long x = k - X0, z = l - Z0;
            return x >= 0 && z >= 0 && x < Width && z < Depth && Wet[z * Width + x] != 0;
        }

        /// <summary>TerrainUtils.SampleHeightInternal on the snapshot. It gives NaN when a needed texel is outside the rectangle.</summary>
        public float Sample(float3 world)
        {
            // ToHeightmapSpace: (worldPosition + data.offset) * data.scale, x and z lanes
            float hx = (world.x + Offset.x) * Scale.x;
            float hz = (world.z + Offset.z) * Scale.z;
            int ix = (int)math.floor(hx), iz = (int)math.floor(hz);
            int ix1 = ix + 1, iz1 = iz + 1;
            ix = ClampI(ix, 0, ResX - 1);
            iz = ClampI(iz, 0, ResZ - 1);
            ix1 = ClampI(ix1, 0, ResX - 1);
            iz1 = ClampI(iz1, 0, ResZ - 1);
            int x0 = ix - X0, x1 = ix1 - X0, z0 = iz - Z0, z1 = iz1 - Z0;
            if (x0 < 0 || x1 >= Width || z0 < 0 || z1 >= Depth || x1 < 0 || z1 < 0 || x0 >= Width || z0 >= Depth)
                return float.NaN;
            float a = Heights[z0 * Width + x0];   // float4.x: (iz, ix)
            float b = Heights[z0 * Width + x1];   // float4.y: (iz, ix + 1)
            float c = Heights[z1 * Width + x0];   // float4.z: (iz + 1, ix)
            float d = Heights[z1 * Width + x1];   // float4.w: (iz + 1, ix + 1)
            float tx = math.saturate(hx - ix), tz = math.saturate(hz - iz);
            float r0 = math.lerp(a, b, tx);       // lerp(float4.xz, float4.yw, t.x)
            float r1 = math.lerp(c, d, tx);
            return ToWorld(math.lerp(r0, r1, tz));
        }

        private static long Clamp(long v, long lo, long hi) => v < lo ? lo : v > hi ? hi : v;

        private static int ClampI(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;
    }

    /// <summary>IHeightSampler over a HeightSnapshot for SkiRunRater.Rate. It counts reads that fell outside the rectangle (NaN).</summary>
    public struct SnapshotSampler : IHeightSampler
    {
        public HeightSnapshot Snap;
        public int Misses;

        public SnapshotSampler(HeightSnapshot snap)
        {
            Snap = snap;
            Misses = 0;
        }

        public float Sample(float3 world)
        {
            float h = Snap.Sample(world);
            if (float.IsNaN(h))
                Misses++;
            return h;
        }
    }
}
