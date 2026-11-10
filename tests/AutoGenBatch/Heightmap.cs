// Heightmap (A1): the 16-bit gray PNG reader and the window snapshot for the real-heightmap fixtures (stage-a-gates 1.2).
// The fixture PNGs are 4096 x 4096, big-endian 16-bit gray, north-up (row 0 = +z). The center of texel (col, row) is at
// x = -7168 + (col + 0.5) x 3.5 and z = 7168 - (row + 0.5) x 3.5. The host flips the rows into the game's south-up lattice
// (l = 4095 - row) and keeps Terrain.Scale / Terrain.Offset. So a texel's world position is the game's own.
// Heights: elev_m = offset + value / 65535 x scale. The snapshot keeps the raw value and carries the sidecar offset in Offset.y
// (ToWorld = raw / Scale.y - Offset.y, so Offset.y = -offset).
// The reader decodes row by row (net8 ZLibStream, the five filter types). It copies only the requested window, with no full-map array.
// Suite HeightmapTests checks filter types and bit layout on generated PNGs, the window, and (when the fixture exists) the two Mountain Village
// field-logged corner heights within 1 m.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using SkiResortMod.Systems;
using Unity.Mathematics;

namespace AutoGenBatch
{
    /// <summary>A fixture PNG on disk and what its sidecar says about the heights.</summary>
    internal sealed class HeightmapFile
    {
        public string Name, Path;
        public float ScaleM = 4096f, OffsetM;
    }

    internal static class Heightmap
    {
        public const int Res = Terrain.Res;

        /// <summary>The fixture folder: %SKI_FIXTURES% when set, else %LOCALAPPDATA%\ModularSkiResort\fixtures.</summary>
        public static string FixtureDir()
        {
            string env = Environment.GetEnvironmentVariable("SKI_FIXTURES");
            if (!string.IsNullOrEmpty(env))
                return env;
            string lad = Environment.GetEnvironmentVariable("LOCALAPPDATA");
            return string.IsNullOrEmpty(lad) ? null : System.IO.Path.Combine(lad, "ModularSkiResort", "fixtures");
        }

        /// <summary>Opens fixture <name> (folder/&lt;name&gt;/&lt;name&gt;Playable.png + a *_build.json or *_terrain.json sidecar).
        /// False with a reason when the folder, the PNG or the sidecar is missing or unreadable.</summary>
        public static bool TryOpen(string name, out HeightmapFile file, out string why)
        {
            file = null;
            string dir = FixtureDir();
            if (dir == null || !Directory.Exists(dir))
            {
                why = "fixture folder not found";
                return false;
            }
            string d = System.IO.Path.Combine(dir, name);
            string png = System.IO.Path.Combine(d, name + "Playable.png");
            if (!File.Exists(png))
            {
                why = name + "Playable.png not found";
                return false;
            }
            var f = new HeightmapFile { Name = name, Path = png };
            string side = System.IO.Path.Combine(d, name + "_build.json");
            if (!File.Exists(side))
                side = System.IO.Path.Combine(d, name + "_terrain.json");
            if (!File.Exists(side))
            {
                why = "sidecar json not found";
                return false;
            }
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(File.ReadAllText(side)))
                {
                    JsonElement root = doc.RootElement;
                    if (root.TryGetProperty("baseLevelM", out JsonElement b))
                    {
                        f.OffsetM = (float)b.GetDouble();   // real resorts: elev = base + value / 65535 x heightScaleM
                        f.ScaleM = root.TryGetProperty("heightScaleM", out JsonElement s) ? (float)s.GetDouble() : 4096f;
                    }
                    else if (root.TryGetProperty("heightScaleOffset", out JsonElement hso) && hso.GetArrayLength() == 2)
                    {
                        f.ScaleM = (float)hso[0].GetDouble();   // vanilla maps: the shift is already in the PNG values
                        f.OffsetM = (float)hso[1].GetDouble();
                    }
                    else
                    {
                        why = "sidecar has neither baseLevelM nor heightScaleOffset";
                        return false;
                    }
                }
            }
            catch (Exception e)
            {
                why = "sidecar unreadable: " + e.Message;
                return false;
            }
            file = f;
            why = null;
            return true;
        }

        /// <summary>A HeightSnapshot of the rectangle PlanGeometry.SnapshotRect asks for (the same window the tool copies), read
        /// from the fixture PNG. Null when the region is degenerate.</summary>
        public static HeightSnapshot Snapshot(HeightmapFile f, float2[] region)
        {
            float3 scale = new float3(Terrain.Scale.x, 65535f / f.ScaleM, Terrain.Scale.z);
            float3 offset = new float3(Terrain.Offset.x, -f.OffsetM, Terrain.Offset.z);
            if (!PlanGeometry.SnapshotRect(region, Res, Res, scale, offset, out int x0, out int z0, out int w, out int d))
                return null;
            HeightSnapshot s = HeightSnapshot.Allocate(x0, z0, w, d, Res, Res, scale, offset, false);
            // south-up lattice rows z0 .. z0 + d - 1 = PNG rows (Res - 1 - (z0 + d - 1)) .. (Res - 1 - z0)
            int row0 = Res - 1 - (z0 + d - 1);
            ushort[] win = ReadWindow(File.ReadAllBytes(f.Path), x0, row0, w, d);
            for (int l = 0; l < d; l++)
                Array.Copy(win, (d - 1 - l) * w, s.Heights, l * w, w);   // PNG row order is the reverse of the lattice's
            return s;
        }

        /// <summary>World height at (x, z) read through a small window snapshot (the field-logged corner heights test).</summary>
        public static float HeightAt(HeightmapFile f, float x, float z)
        {
            HeightSnapshot s = Snapshot(f, Terrain.Square(x, z, 40f));
            return s == null ? float.NaN : s.Sample(new float3(x, 0f, z));
        }

        // ---------------------------------------------------------------- PNG

        private static readonly byte[] Signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        /// <summary>Decodes only columns [col0, col0 + w) of PNG rows [row0, row0 + h) of a non-interlaced 16-bit gray PNG into a
        /// row-major array (row 0 of the result = PNG row row0). Throws InvalidDataException for anything else.</summary>
        public static ushort[] ReadWindow(byte[] png, int col0, int row0, int w, int h)
        {
            var idat = new MemoryStream();
            int width = 0, height = 0;
            bool ihdr = false;
            if (png.Length < 8)
                throw new InvalidDataException("not a PNG");
            for (int i = 0; i < 8; i++)
                if (png[i] != Signature[i])
                    throw new InvalidDataException("not a PNG");
            int p = 8;
            while (p + 12 <= png.Length)
            {
                int len = ReadInt(png, p);
                string type = System.Text.Encoding.ASCII.GetString(png, p + 4, 4);
                if (len < 0 || p + 12L + len > png.Length)
                    throw new InvalidDataException("truncated chunk " + type);
                if (type == "IHDR")
                {
                    width = ReadInt(png, p + 8);
                    height = ReadInt(png, p + 12);
                    byte depth = png[p + 16], color = png[p + 17], interlace = png[p + 20];
                    if (depth != 16 || color != 0 || interlace != 0)
                        throw new InvalidDataException($"only non-interlaced 16-bit gray PNGs (depth {depth}, color {color}, interlace {interlace})");
                    ihdr = true;
                }
                else if (type == "IDAT")
                    idat.Write(png, p + 8, len);
                else if (type == "IEND")
                    break;
                p += 12 + len;
            }
            if (!ihdr || width <= 0 || height <= 0)
                throw new InvalidDataException("no IHDR");
            if (col0 < 0 || row0 < 0 || w < 0 || h < 0 || col0 + w > width || row0 + h > height)
                throw new ArgumentOutOfRangeException(nameof(w), "window outside the image");
            var result = new ushort[w * h];
            int stride = width * 2;
            byte[] prev = new byte[stride], cur = new byte[stride];
            idat.Position = 0;
            using (var z = new ZLibStream(idat, CompressionMode.Decompress))
            {
                for (int row = 0; row < height && row < row0 + h; row++)
                {
                    int ft = z.ReadByte();
                    if (ft < 0)
                        throw new InvalidDataException("compressed data ends at row " + row);
                    ReadFully(z, cur, stride);
                    Unfilter(ft, cur, prev, 2);
                    if (row >= row0)
                    {
                        int o = (row - row0) * w;
                        for (int c = 0; c < w; c++)
                            result[o + c] = (ushort)((cur[(col0 + c) * 2] << 8) | cur[(col0 + c) * 2 + 1]);
                    }
                    byte[] t = prev;
                    prev = cur;
                    cur = t;
                }
            }
            return result;
        }

        private static void Unfilter(int type, byte[] cur, byte[] prev, int bpp)
        {
            int n = cur.Length;
            switch (type)
            {
                case 0:
                    break;
                case 1:   // Sub
                    for (int i = bpp; i < n; i++)
                        cur[i] = (byte)(cur[i] + cur[i - bpp]);
                    break;
                case 2:   // Up
                    for (int i = 0; i < n; i++)
                        cur[i] = (byte)(cur[i] + prev[i]);
                    break;
                case 3:   // Average
                    for (int i = 0; i < n; i++)
                    {
                        int a = i >= bpp ? cur[i - bpp] : 0;
                        cur[i] = (byte)(cur[i] + ((a + prev[i]) >> 1));
                    }
                    break;
                case 4:   // Paeth
                    for (int i = 0; i < n; i++)
                    {
                        int a = i >= bpp ? cur[i - bpp] : 0, b = prev[i], c = i >= bpp ? prev[i - bpp] : 0;
                        int pa = Math.Abs(b - c), pb = Math.Abs(a - c), pc = Math.Abs(a + b - 2 * c);
                        int pr = pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
                        cur[i] = (byte)(cur[i] + pr);
                    }
                    break;
                default:
                    throw new InvalidDataException("filter type " + type);
            }
        }

        private static void ReadFully(Stream s, byte[] buf, int n)
        {
            int got = 0;
            while (got < n)
            {
                int r = s.Read(buf, got, n - got);
                if (r <= 0)
                    throw new InvalidDataException("compressed data ends inside a row");
                got += r;
            }
        }

        private static int ReadInt(byte[] b, int o) => (b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3];

        // ---------------------------------------------------------------- encoder (tests only)

        /// <summary>Encodes a 16-bit gray PNG with a chosen filter type per row (filters[row % filters.Length]). The test fixtures use it. rows &lt; height writes
        /// only the first rows (the IHDR still says height). ReadWindow stops after the window's last row, so a short stream is enough.</summary>
        public static byte[] EncodeGray16(ushort[] v, int width, int height, int[] filters, int rows = -1)
        {
            int stride = width * 2;
            var raw = new MemoryStream();
            byte[] prev = new byte[stride];
            for (int row = 0; row < (rows < 0 ? height : rows); row++)
            {
                var cur = new byte[stride];
                for (int c = 0; c < width; c++)
                {
                    cur[c * 2] = (byte)(v[row * width + c] >> 8);
                    cur[c * 2 + 1] = (byte)v[row * width + c];
                }
                int ft = filters[row % filters.Length];
                var f = new byte[stride];
                for (int i = 0; i < stride; i++)
                {
                    int a = i >= 2 ? cur[i - 2] : 0, b = prev[i], c = i >= 2 ? prev[i - 2] : 0, pred;
                    switch (ft)
                    {
                        case 1: pred = a; break;
                        case 2: pred = b; break;
                        case 3: pred = (a + b) >> 1; break;
                        case 4:
                            int pa = Math.Abs(b - c), pb = Math.Abs(a - c), pc = Math.Abs(a + b - 2 * c);
                            pred = pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
                            break;
                        default: pred = 0; break;
                    }
                    f[i] = (byte)(cur[i] - pred);
                }
                raw.WriteByte((byte)ft);
                raw.Write(f, 0, stride);
                prev = cur;
            }
            var comp = new MemoryStream();
            using (var z = new ZLibStream(comp, CompressionLevel.Fastest, true))
                z.Write(raw.GetBuffer(), 0, (int)raw.Length);
            var png = new MemoryStream();
            png.Write(Signature, 0, 8);
            var ihdr = new byte[13];
            WriteInt(ihdr, 0, width);
            WriteInt(ihdr, 4, height);
            ihdr[8] = 16;   // depth. Color type 0 = gray. Compression, filter, interlace 0
            Chunk(png, "IHDR", ihdr, 13);
            Chunk(png, "IDAT", comp.GetBuffer(), (int)comp.Length);
            Chunk(png, "IEND", new byte[0], 0);
            return png.ToArray();
        }

        private static void Chunk(Stream s, string type, byte[] data, int n)
        {
            var len = new byte[4];
            WriteInt(len, 0, n);
            s.Write(len, 0, 4);
            byte[] t = System.Text.Encoding.ASCII.GetBytes(type);
            s.Write(t, 0, 4);
            s.Write(data, 0, n);
            uint crc = 0xFFFFFFFFu;
            foreach (byte b in t)
                crc = Crc(crc, b);
            for (int i = 0; i < n; i++)
                crc = Crc(crc, data[i]);
            var c = new byte[4];
            WriteInt(c, 0, (int)~crc);
            s.Write(c, 0, 4);
        }

        private static uint Crc(uint crc, byte b)
        {
            crc ^= b;
            for (int k = 0; k < 8; k++)
                crc = (crc & 1u) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
            return crc;
        }

        private static void WriteInt(byte[] b, int o, int v)
        {
            b[o] = (byte)(v >> 24);
            b[o + 1] = (byte)(v >> 16);
            b[o + 2] = (byte)(v >> 8);
            b[o + 3] = (byte)v;
        }
    }

    internal static partial class Program
    {
        private static void HeightmapTests()
        {
            // The five filter types, 16-bit big-endian samples (values with a high byte), a window anywhere in the image
            const int W = 37, H = 29;
            var rng = new Unity.Mathematics.Random(77u);
            var v = new ushort[W * H];
            for (int i = 0; i < v.Length; i++)
                v[i] = (ushort)(((i % W) * 1531 + (i / W) * 977 + rng.NextInt(0, 3000)) & 0xFFFF);
            for (int ft = 0; ft <= 4; ft++)
            {
                byte[] png = Heightmap.EncodeGray16(v, W, H, new[] { ft });
                ushort[] all = Heightmap.ReadWindow(png, 0, 0, W, H);
                Check(Same(all, v), "Heightmap", $"filter type {ft}: full image differs");
            }
            byte[] mixed = Heightmap.EncodeGray16(v, W, H, new[] { 0, 1, 2, 3, 4 });
            ushort[] win = Heightmap.ReadWindow(mixed, 5, 7, 11, 9);
            bool winOk = true;
            for (int r = 0; r < 9; r++)
                for (int c = 0; c < 11; c++)
                    winOk &= win[r * 11 + c] == v[(7 + r) * W + 5 + c];
            Check(winOk, "Heightmap", "mixed filters: window differs from the source");
            Check(win[0] == v[7 * W + 5] && v[7 * W + 5] > 255, "Heightmap", "samples are not 16-bit big-endian");
            bool threw = false;
            try
            {
                Heightmap.ReadWindow(mixed, 30, 0, 11, 2);
            }
            catch (ArgumentOutOfRangeException)
            {
                threw = true;
            }
            Check(threw, "Heightmap", "a window outside the image is accepted");
            threw = false;
            try
            {
                Heightmap.ReadWindow(new byte[] { 1, 2, 3 }, 0, 0, 1, 1);
            }
            catch (InvalidDataException)
            {
                threw = true;
            }
            Check(threw, "Heightmap", "garbage accepted as a PNG");

            // Snapshot geometry on a generated fixture: a PNG row r (north-up) must land on lattice row 4095 - r with the sidecar offset
            string dir = Path.Combine(Path.GetTempPath(), "ski-heightmap-test-" + Guid.NewGuid().ToString("N"));
            string old = Environment.GetEnvironmentVariable("SKI_FIXTURES");
            try
            {
                Directory.CreateDirectory(Path.Combine(dir, "Gen"));
                const int N = Heightmap.Res;
                const int Rows = 200;   // a window at z = +6850 needs PNG rows 22-160 only: a short stream keeps the default run fast
                var full = new ushort[Rows * N];
                for (int r = 0; r < Rows; r++)
                    for (int c = 0; c < N; c++)
                        full[r * N + c] = (ushort)(1000 + (N - 1 - r) * 8 + (c >> 3));   // grows toward +z (north), a little toward +x
                File.WriteAllBytes(Path.Combine(dir, "Gen", "GenPlayable.png"), Heightmap.EncodeGray16(full, N, N, new[] { 4, 2, 0, 3, 1 }, Rows));
                File.WriteAllText(Path.Combine(dir, "Gen", "Gen_build.json"), "{\"baseLevelM\": 100.5, \"heightScaleM\": 4096.0}");
                Environment.SetEnvironmentVariable("SKI_FIXTURES", dir);
                Check(Heightmap.TryOpen("Gen", out HeightmapFile gen, out string why), "Heightmap", "TryOpen: " + why);
                if (gen != null)
                {
                    Check(gen.OffsetM == 100.5f && gen.ScaleM == 4096f, "Heightmap", $"sidecar offset {gen.OffsetM} scale {gen.ScaleM}");
                    float2[] region = Terrain.Square(0f, 6850f, 200f);
                    HeightSnapshot s = Heightmap.Snapshot(gen, region);
                    Check(s != null && s.Valid, "Heightmap", "snapshot not valid");
                    if (s != null && s.Valid)
                    {
                        int k = s.X0 + 20, l = s.Z0 + 33;   // lattice texel (k, l): row r = 4095 - l, column k
                        s.TryRaw(k, l, out ushort raw);
                        Check(raw == full[(N - 1 - l) * N + k], "Heightmap", $"lattice ({k}, {l}) holds {raw}, PNG has {full[(N - 1 - l) * N + k]} (row flip)");
                        float x = (float)(s.Origin.x + k * (double)s.TexelM), z = (float)(s.Origin.y + l * (double)s.TexelM);
                        float want = 100.5f + raw / 65535f * 4096f;
                        Check(Math.Abs(s.Sample(new float3(x, 0f, z)) - want) < 0.01f, "Heightmap", $"world height {s.Sample(new float3(x, 0f, z))} vs {want}");
                        Check(Math.Abs(z - (7168.0 - (N - 1 - l + 0.5) * 3.5)) < 1e-3 && Math.Abs(x - (-7168.0 + (k + 0.5) * 3.5)) < 1e-3, "Heightmap",
                            "texel center is not the header contract's x = -7168 + (col + 0.5) x 3.5, z = 7168 - (row + 0.5) x 3.5");
                    }
                }
            }
            finally
            {
                Environment.SetEnvironmentVariable("SKI_FIXTURES", old);
                try { Directory.Delete(dir, true); } catch (IOException) { }
            }

            // The two Mountain Village field-logged corner heights (field-test 3 log, research decode 2144.03 / 1239.64, [E] 0.2)
            if (!Heightmap.TryOpen("MountainVillage", out HeightmapFile mv, out string skip))
            {
                Console.WriteLine("SKIP  Heightmap MountainVillage corner heights: " + skip);
                return;
            }
            float c1 = Heightmap.HeightAt(mv, 6365f, -1073f), c2 = Heightmap.HeightAt(mv, 3426f, -1984f);
            Check(Math.Abs(c1 - 2144.0f) <= 1f, "Heightmap MountainVillage", $"corner (6365, -1073) reads {c1:F2} m, field log 2144.0 m");
            Check(Math.Abs(c2 - 1240.4f) <= 1f, "Heightmap MountainVillage", $"corner (3426, -1984) reads {c2:F2} m, field log 1240.4 m");
            Console.WriteLine($"INFO  Heightmap MountainVillage: corner heights {c1:F2} / {c2:F2} m (field log 2144.0 / 1240.4, research decode 2144.03 / 1239.64)");
        }

        private static bool Same(ushort[] a, ushort[] b)
        {
            if (a.Length != b.Length)
                return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i])
                    return false;
            return true;
        }
    }
}
