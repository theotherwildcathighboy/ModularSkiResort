// Pure pixel work for a notification icon texture built at runtime (LiftLineIconAsset): resize, mip chain, and the block encoders the
// vanilla icon formats need. No Unity types. tests/LiftBatch LI-4..LI-6 round-trip every encoder through its decoder.
// Why: NotificationIconRenderSystem copies every icon prefab's m_Icon into ONE Texture2DArray with Graphics.CopyTexture (all mips, raw
// blocks). The array is sized to the largest icon and in the format of the last one it reads (NotificationIconRenderSystem.cs:207-245). An
// icon of another size or format would break that copy for every icon. So ours must match the vanilla icon texture exactly: size, mip count
// and format. Pixels are RGBA bytes, row 0 first, in the texture's own row order (Unity: bottom row first). The encoders keep it.

using System;
using System.Collections.Generic;

namespace SkiResortMod.Systems
{
    /// <summary>The texture formats we can build. The names follow UnityEngine.TextureFormat.</summary>
    public enum IconTexFormat : byte { Unsupported = 0, RGBA32 = 1, ARGB32 = 2, DXT5 = 3, BC7 = 4 }

    public static class IconTextureCodec
    {
        /// <summary>BC7 4-bit index weights (the BC7 specification's aWeight4 table).</summary>
        private static readonly int[] Weights4 = { 0, 4, 9, 13, 17, 21, 26, 30, 34, 38, 43, 47, 51, 55, 60, 64 };

        /// <summary>The mip count of a full chain for a w x h texture (Unity's mipChain: true).</summary>
        public static int FullMipCount(int w, int h)
        {
            int n = 1;
            int s = Math.Max(w, h);
            while (s > 1)
            {
                s >>= 1;
                n++;
            }
            return n;
        }

        /// <summary>Bytes of one encoded mip level.</summary>
        public static int LevelBytes(IconTexFormat f, int w, int h)
        {
            switch (f)
            {
                case IconTexFormat.RGBA32:
                case IconTexFormat.ARGB32:
                    return w * h * 4;
                case IconTexFormat.DXT5:
                case IconTexFormat.BC7:
                    return Math.Max(1, (w + 3) / 4) * Math.Max(1, (h + 3) / 4) * 16;
                default:
                    return 0;
            }
        }

        /// <summary>Resamples RGBA to nw x nh with an alpha-weighted box filter over each target pixel's footprint. Any ratio works, and a
        /// pure downscale by an integer factor is an exact box. Transparent pixels add no color, so edges keep no dark fringe.</summary>
        public static byte[] Resize(byte[] rgba, int w, int h, int nw, int nh)
        {
            if (rgba == null || rgba.Length < w * h * 4)
                throw new ArgumentException("rgba too short");
            var dst = new byte[nw * nh * 4];
            double sx = (double)w / nw, sy = (double)h / nh;
            for (int y = 0; y < nh; y++)
            {
                double y0 = y * sy, y1 = (y + 1) * sy;
                for (int x = 0; x < nw; x++)
                {
                    double x0 = x * sx, x1 = (x + 1) * sx;
                    double r = 0, g = 0, b = 0, a = 0, area = 0;
                    for (int py = (int)Math.Floor(y0); py < Math.Min(h, (int)Math.Ceiling(y1)); py++)
                    {
                        double wy = Math.Min(y1, py + 1) - Math.Max(y0, py);
                        if (wy <= 0)
                            continue;
                        for (int px = (int)Math.Floor(x0); px < Math.Min(w, (int)Math.Ceiling(x1)); px++)
                        {
                            double wx = Math.Min(x1, px + 1) - Math.Max(x0, px);
                            if (wx <= 0)
                                continue;
                            double wgt = wx * wy;
                            int i = (py * w + px) * 4;
                            double al = rgba[i + 3] / 255.0;
                            r += rgba[i] * al * wgt;
                            g += rgba[i + 1] * al * wgt;
                            b += rgba[i + 2] * al * wgt;
                            a += al * wgt;
                            area += wgt;
                        }
                    }
                    int o = (y * nw + x) * 4;
                    if (area <= 0)
                        continue;
                    if (a > 0)
                    {
                        dst[o] = ToByte(r / a);
                        dst[o + 1] = ToByte(g / a);
                        dst[o + 2] = ToByte(b / a);
                    }
                    dst[o + 3] = ToByte(a / area * 255.0);
                }
            }
            return dst;
        }

        /// <summary>The full chain: level 0 = the input, each next level half the size (at least 1), alpha-weighted box.</summary>
        public static List<byte[]> MipChain(byte[] rgba, int w, int h, int count)
        {
            var levels = new List<byte[]>(count) { rgba };
            int cw = w, ch = h;
            for (int m = 1; m < count; m++)
            {
                int nw = Math.Max(1, cw >> 1), nh = Math.Max(1, ch >> 1);
                levels.Add(Resize(levels[m - 1], cw, ch, nw, nh));
                cw = nw;
                ch = nh;
            }
            return levels;
        }

        /// <summary>One mip level in the target format. Null for Unsupported.</summary>
        public static byte[] EncodeLevel(IconTexFormat f, byte[] rgba, int w, int h)
        {
            switch (f)
            {
                case IconTexFormat.RGBA32:
                    return (byte[])rgba.Clone();
                case IconTexFormat.ARGB32:
                {
                    var o = new byte[rgba.Length];
                    for (int i = 0; i < rgba.Length; i += 4)
                    {
                        o[i] = rgba[i + 3];
                        o[i + 1] = rgba[i];
                        o[i + 2] = rgba[i + 1];
                        o[i + 3] = rgba[i + 2];
                    }
                    return o;
                }
                case IconTexFormat.DXT5:
                    return EncodeDxt5(rgba, w, h);
                case IconTexFormat.BC7:
                    return EncodeBc7(rgba, w, h);
                default:
                    return null;
            }
        }

        // ---------------------------------------------------------------------------------------------------------------------------
        // BC7, mode 6 only: one subset, RGBA 7.7.7.7 endpoints with a p-bit each, 4-bit indices. Good for an icon of flat color areas.

        public static byte[] EncodeBc7(byte[] rgba, int w, int h)
        {
            int bw = Math.Max(1, (w + 3) / 4), bh = Math.Max(1, (h + 3) / 4);
            var o = new byte[bw * bh * 16];
            var px = new int[64];
            for (int by = 0; by < bh; by++)
                for (int bx = 0; bx < bw; bx++)
                {
                    Gather(rgba, w, h, bx, by, px);
                    EncodeBc7Block(px, o, (by * bw + bx) * 16);
                }
            return o;
        }

        public static byte[] DecodeBc7(byte[] blocks, int w, int h)
        {
            int bw = Math.Max(1, (w + 3) / 4), bh = Math.Max(1, (h + 3) / 4);
            var o = new byte[w * h * 4];
            var px = new int[64];
            for (int by = 0; by < bh; by++)
                for (int bx = 0; bx < bw; bx++)
                {
                    DecodeBc7Block(blocks, (by * bw + bx) * 16, px);
                    Scatter(px, o, w, h, bx, by);
                }
            return o;
        }

        private static void EncodeBc7Block(int[] px, byte[] o, int at)
        {
            // Principal axis of the 16 RGBA points, endpoints at the extreme projections, then one least-squares refit.
            Axis(px, 4, out double[] mean, out double[] axis);
            double tmin = double.MaxValue, tmax = double.MinValue;
            for (int i = 0; i < 16; i++)
            {
                double t = 0;
                for (int c = 0; c < 4; c++)
                    t += (px[i * 4 + c] - mean[c]) * axis[c];
                tmin = Math.Min(tmin, t);
                tmax = Math.Max(tmax, t);
            }
            var e0 = new double[4];
            var e1 = new double[4];
            for (int c = 0; c < 4; c++)
            {
                e0[c] = mean[c] + axis[c] * tmin;
                e1[c] = mean[c] + axis[c] * tmax;
            }
            var best = new byte[16];
            long bestErr = TryBc7(px, e0, e1, best);
            var idx = new int[16];
            for (int iter = 0; iter < 2; iter++)
            {
                for (int i = 0; i < 16; i++)
                    idx[i] = Bc7Index(best, i);
                if (!RefitEndpoints(px, 4, idx, Weights4, 64, e0, e1))
                    break;
                var trial = new byte[16];
                long err = TryBc7(px, e0, e1, trial);
                if (err >= bestErr)
                    break;
                bestErr = err;
                best = trial;
            }
            Buffer.BlockCopy(best, 0, o, at, 16);
        }

        /// <summary>Alpha error weight in the BC7 search: an opaque area must stay exactly opaque (255 needs p-bit 1) and a clear one clear,
        /// at the cost of one step of color.</summary>
        private const long AlphaWeight = 8;

        /// <summary>Quantizes the endpoints under each of the four p-bit pairs, picks the indices, and writes the best block. Returns its
        /// (alpha-weighted) error.</summary>
        private static long TryBc7(int[] px, double[] e0, double[] e1, byte[] block)
        {
            long bestErr = long.MaxValue;
            var q0 = new int[4];
            var q1 = new int[4];
            var idx = new int[16];
            int p0 = 0, p1 = 0;
            var t0 = new int[4];
            var t1 = new int[4];
            var tIdx = new int[16];
            var pal = new int[16 * 4];
            for (int pp = 0; pp < 4; pp++)
            {
                int a0 = pp & 1, a1 = pp >> 1;
                QuantizeWithP(e0, a0, t0);
                QuantizeWithP(e1, a1, t1);
                for (int k = 0; k < 16; k++)
                    for (int c = 0; c < 4; c++)
                        pal[k * 4 + c] = ((64 - Weights4[k]) * ((t0[c] << 1) | a0) + Weights4[k] * ((t1[c] << 1) | a1) + 32) >> 6;
                long sum = 0;
                for (int i = 0; i < 16 && sum < bestErr; i++)
                {
                    long bestE = long.MaxValue;
                    for (int k = 0; k < 16; k++)
                    {
                        long e = 0;
                        for (int c = 0; c < 4; c++)
                        {
                            long d = px[i * 4 + c] - pal[k * 4 + c];
                            e += c == 3 ? d * d * AlphaWeight : d * d;
                        }
                        if (e < bestE)
                        {
                            bestE = e;
                            tIdx[i] = k;
                        }
                    }
                    sum += bestE;
                }
                if (sum < bestErr)
                {
                    bestErr = sum;
                    Array.Copy(t0, q0, 4);
                    Array.Copy(t1, q1, 4);
                    Array.Copy(tIdx, idx, 16);
                    p0 = a0;
                    p1 = a1;
                }
            }
            long err = bestErr;
            if (idx[0] >= 8)
            {
                // The anchor index has no top bit: swap the endpoints and invert every index.
                for (int c = 0; c < 4; c++)
                {
                    int t = q0[c];
                    q0[c] = q1[c];
                    q1[c] = t;
                }
                int tp = p0;
                p0 = p1;
                p1 = tp;
                for (int i = 0; i < 16; i++)
                    idx[i] = 15 - idx[i];
            }
            Array.Clear(block, 0, 16);
            int bit = 0;
            Put(block, ref bit, 1 << 6, 7);              // mode 6: six 0 bits then a 1
            for (int c = 0; c < 4; c++)
            {
                Put(block, ref bit, q0[c], 7);
                Put(block, ref bit, q1[c], 7);
            }
            Put(block, ref bit, p0, 1);
            Put(block, ref bit, p1, 1);
            Put(block, ref bit, idx[0], 3);
            for (int i = 1; i < 16; i++)
                Put(block, ref bit, idx[i], 4);
            return err;
        }

        /// <summary>7-bit values for one endpoint under the given p-bit (the decoded value is (v &lt;&lt; 1) | p).</summary>
        private static void QuantizeWithP(double[] e, int p, int[] q)
        {
            for (int c = 0; c < 4; c++)
            {
                int v = (int)Math.Round((Clamp(e[c], 0, 255) - p) / 2.0);
                q[c] = Math.Max(0, Math.Min(127, v));
            }
        }

        private static int Bc7Index(byte[] block, int i)
        {
            int bit = 65 + (i == 0 ? 0 : 3 + (i - 1) * 4);
            return Get(block, bit, i == 0 ? 3 : 4);
        }

        private static void DecodeBc7Block(byte[] blocks, int at, int[] px)
        {
            var block = new byte[16];
            Buffer.BlockCopy(blocks, at, block, 0, 16);
            if ((block[0] & 0x7F) != 0x40)
            {
                for (int i = 0; i < 64; i++)
                    px[i] = 0;   // not mode 6: this decoder reads only what EncodeBc7 writes
                return;
            }
            int bit = 7;
            var q0 = new int[4];
            var q1 = new int[4];
            for (int c = 0; c < 4; c++)
            {
                q0[c] = Get(block, bit, 7);
                bit += 7;
                q1[c] = Get(block, bit, 7);
                bit += 7;
            }
            int p0 = Get(block, bit, 1);
            int p1 = Get(block, bit + 1, 1);
            for (int i = 0; i < 16; i++)
            {
                int k = Bc7Index(block, i);
                for (int c = 0; c < 4; c++)
                {
                    int a = (q0[c] << 1) | p0, b = (q1[c] << 1) | p1;
                    px[i * 4 + c] = ((64 - Weights4[k]) * a + Weights4[k] * b + 32) >> 6;
                }
            }
        }

        // ---------------------------------------------------------------------------------------------------------------------------
        // DXT5 (BC3): an 8-level alpha block, then a BC1 color block that BC3 always reads in four-color mode.

        public static byte[] EncodeDxt5(byte[] rgba, int w, int h)
        {
            int bw = Math.Max(1, (w + 3) / 4), bh = Math.Max(1, (h + 3) / 4);
            var o = new byte[bw * bh * 16];
            var px = new int[64];
            for (int by = 0; by < bh; by++)
                for (int bx = 0; bx < bw; bx++)
                {
                    Gather(rgba, w, h, bx, by, px);
                    int at = (by * bw + bx) * 16;
                    EncodeAlphaBlock(px, o, at);
                    EncodeColorBlock(px, o, at + 8);
                }
            return o;
        }

        public static byte[] DecodeDxt5(byte[] blocks, int w, int h)
        {
            int bw = Math.Max(1, (w + 3) / 4), bh = Math.Max(1, (h + 3) / 4);
            var o = new byte[w * h * 4];
            var px = new int[64];
            for (int by = 0; by < bh; by++)
                for (int bx = 0; bx < bw; bx++)
                {
                    int at = (by * bw + bx) * 16;
                    var al = AlphaPalette(blocks[at], blocks[at + 1]);
                    ulong abits = 0;
                    for (int k = 0; k < 6; k++)
                        abits |= (ulong)blocks[at + 2 + k] << (8 * k);
                    var pal = ColorPalette(blocks[at + 8] | (blocks[at + 9] << 8), blocks[at + 10] | (blocks[at + 11] << 8));
                    uint cbits = (uint)(blocks[at + 12] | (blocks[at + 13] << 8) | (blocks[at + 14] << 16) | (blocks[at + 15] << 24));
                    for (int i = 0; i < 16; i++)
                    {
                        int ci = (int)((cbits >> (2 * i)) & 3);
                        int ai = (int)((abits >> (3 * i)) & 7);
                        px[i * 4] = pal[ci * 3];
                        px[i * 4 + 1] = pal[ci * 3 + 1];
                        px[i * 4 + 2] = pal[ci * 3 + 2];
                        px[i * 4 + 3] = al[ai];
                    }
                    Scatter(px, o, w, h, bx, by);
                }
            return o;
        }

        private static void EncodeAlphaBlock(int[] px, byte[] o, int at)
        {
            int amin = 255, amax = 0;
            for (int i = 0; i < 16; i++)
            {
                amin = Math.Min(amin, px[i * 4 + 3]);
                amax = Math.Max(amax, px[i * 4 + 3]);
            }
            // a0 > a1 selects the 8-level palette. Equal values need no indices (all 0 = a0).
            int a0 = amax, a1 = amin;
            if (a0 == a1)
            {
                o[at] = (byte)a0;
                o[at + 1] = (byte)a1;
                return;
            }
            var pal = AlphaPalette((byte)a0, (byte)a1);
            ulong bits = 0;
            for (int i = 0; i < 16; i++)
            {
                int a = px[i * 4 + 3], best = 0, bestD = int.MaxValue;
                for (int k = 0; k < 8; k++)
                {
                    int d = Math.Abs(a - pal[k]);
                    if (d < bestD)
                    {
                        bestD = d;
                        best = k;
                    }
                }
                bits |= (ulong)best << (3 * i);
            }
            o[at] = (byte)a0;
            o[at + 1] = (byte)a1;
            for (int k = 0; k < 6; k++)
                o[at + 2 + k] = (byte)(bits >> (8 * k));
        }

        private static int[] AlphaPalette(byte a0, byte a1)
        {
            var p = new int[8];
            p[0] = a0;
            p[1] = a1;
            if (a0 > a1)
            {
                for (int k = 1; k < 7; k++)
                    p[k + 1] = ((7 - k) * a0 + k * a1) / 7;
            }
            else
            {
                for (int k = 1; k < 5; k++)
                    p[k + 1] = ((5 - k) * a0 + k * a1) / 5;
                p[6] = 0;
                p[7] = 255;
            }
            return p;
        }

        private static void EncodeColorBlock(int[] px, byte[] o, int at)
        {
            // Color matters only where the pixel shows: weight by alpha (a transparent pixel's color is free).
            var rgb = new int[64];
            int shown = 0;
            for (int i = 0; i < 16; i++)
            {
                rgb[i * 4] = px[i * 4];
                rgb[i * 4 + 1] = px[i * 4 + 1];
                rgb[i * 4 + 2] = px[i * 4 + 2];
                rgb[i * 4 + 3] = px[i * 4 + 3] > 0 ? 1 : 0;
                shown += rgb[i * 4 + 3];
            }
            if (shown == 0)
            {
                for (int i = 0; i < 16; i++)
                    rgb[i * 4 + 3] = 1;
            }
            AxisWeighted(rgb, out double[] mean, out double[] axis);
            double tmin = double.MaxValue, tmax = double.MinValue;
            for (int i = 0; i < 16; i++)
            {
                if (rgb[i * 4 + 3] == 0)
                    continue;
                double t = 0;
                for (int c = 0; c < 3; c++)
                    t += (rgb[i * 4 + c] - mean[c]) * axis[c];
                tmin = Math.Min(tmin, t);
                tmax = Math.Max(tmax, t);
            }
            int c0 = To565(mean[0] + axis[0] * tmax, mean[1] + axis[1] * tmax, mean[2] + axis[2] * tmax);
            int c1 = To565(mean[0] + axis[0] * tmin, mean[1] + axis[1] * tmin, mean[2] + axis[2] * tmin);
            if (c0 < c1)
            {
                int t = c0;
                c0 = c1;
                c1 = t;
            }
            uint bits = 0;
            if (c0 != c1)
            {
                var pal = ColorPalette(c0, c1);
                for (int i = 0; i < 16; i++)
                {
                    int best = 0, bestD = int.MaxValue;
                    for (int k = 0; k < 4; k++)
                    {
                        int dr = px[i * 4] - pal[k * 3], dg = px[i * 4 + 1] - pal[k * 3 + 1], db = px[i * 4 + 2] - pal[k * 3 + 2];
                        int d = dr * dr + dg * dg + db * db;
                        if (d < bestD)
                        {
                            bestD = d;
                            best = k;
                        }
                    }
                    bits |= (uint)best << (2 * i);
                }
            }
            o[at] = (byte)c0;
            o[at + 1] = (byte)(c0 >> 8);
            o[at + 2] = (byte)c1;
            o[at + 3] = (byte)(c1 >> 8);
            o[at + 4] = (byte)bits;
            o[at + 5] = (byte)(bits >> 8);
            o[at + 6] = (byte)(bits >> 16);
            o[at + 7] = (byte)(bits >> 24);
        }

        private static int[] ColorPalette(int c0, int c1)
        {
            var p = new int[12];
            From565(c0, p, 0);
            From565(c1, p, 3);
            for (int c = 0; c < 3; c++)
            {
                p[6 + c] = (2 * p[c] + p[3 + c]) / 3;
                p[9 + c] = (p[c] + 2 * p[3 + c]) / 3;
            }
            return p;
        }

        private static int To565(double r, double g, double b)
        {
            int ri = (int)Math.Round(Clamp(r, 0, 255) * 31 / 255.0);
            int gi = (int)Math.Round(Clamp(g, 0, 255) * 63 / 255.0);
            int bi = (int)Math.Round(Clamp(b, 0, 255) * 31 / 255.0);
            return (ri << 11) | (gi << 5) | bi;
        }

        private static void From565(int c, int[] p, int at)
        {
            int r = (c >> 11) & 31, g = (c >> 5) & 63, b = c & 31;
            p[at] = (r << 3) | (r >> 2);
            p[at + 1] = (g << 2) | (g >> 4);
            p[at + 2] = (b << 3) | (b >> 2);
        }

        // ---------------------------------------------------------------------------------------------------------------------------
        // Shared helpers.

        /// <summary>The 4x4 block at (bx, by) into px (16 x RGBA), edge pixels repeated past the image.</summary>
        private static void Gather(byte[] rgba, int w, int h, int bx, int by, int[] px)
        {
            for (int y = 0; y < 4; y++)
                for (int x = 0; x < 4; x++)
                {
                    int sx = Math.Min(w - 1, bx * 4 + x), sy = Math.Min(h - 1, by * 4 + y);
                    int s = (sy * w + sx) * 4, d = (y * 4 + x) * 4;
                    px[d] = rgba[s];
                    px[d + 1] = rgba[s + 1];
                    px[d + 2] = rgba[s + 2];
                    px[d + 3] = rgba[s + 3];
                }
        }

        private static void Scatter(int[] px, byte[] o, int w, int h, int bx, int by)
        {
            for (int y = 0; y < 4; y++)
                for (int x = 0; x < 4; x++)
                {
                    int dx = bx * 4 + x, dy = by * 4 + y;
                    if (dx >= w || dy >= h)
                        continue;
                    int d = (dy * w + dx) * 4, s = (y * 4 + x) * 4;
                    for (int c = 0; c < 4; c++)
                        o[d + c] = (byte)Math.Max(0, Math.Min(255, px[s + c]));
                }
        }

        /// <summary>Mean and principal axis (power iteration on the covariance) of 16 points with n channels.</summary>
        private static void Axis(int[] px, int n, out double[] mean, out double[] axis)
        {
            mean = new double[4];
            for (int i = 0; i < 16; i++)
                for (int c = 0; c < n; c++)
                    mean[c] += px[i * 4 + c] / 16.0;
            var cov = new double[4, 4];
            for (int i = 0; i < 16; i++)
                for (int a = 0; a < n; a++)
                    for (int b = 0; b < n; b++)
                        cov[a, b] += (px[i * 4 + a] - mean[a]) * (px[i * 4 + b] - mean[b]);
            axis = PowerIterate(cov, n);
        }

        /// <summary>RGB mean and axis over the points whose weight (channel 3) is 1.</summary>
        private static void AxisWeighted(int[] rgbw, out double[] mean, out double[] axis)
        {
            mean = new double[4];
            int count = 0;
            for (int i = 0; i < 16; i++)
            {
                if (rgbw[i * 4 + 3] == 0)
                    continue;
                count++;
                for (int c = 0; c < 3; c++)
                    mean[c] += rgbw[i * 4 + c];
            }
            for (int c = 0; c < 3; c++)
                mean[c] /= Math.Max(1, count);
            var cov = new double[4, 4];
            for (int i = 0; i < 16; i++)
            {
                if (rgbw[i * 4 + 3] == 0)
                    continue;
                for (int a = 0; a < 3; a++)
                    for (int b = 0; b < 3; b++)
                        cov[a, b] += (rgbw[i * 4 + a] - mean[a]) * (rgbw[i * 4 + b] - mean[b]);
            }
            axis = PowerIterate(cov, 3);
        }

        private static double[] PowerIterate(double[,] cov, int n)
        {
            var v = new double[4];
            for (int c = 0; c < n; c++)
                v[c] = 1.0 + c * 0.01;
            for (int it = 0; it < 12; it++)
            {
                var nv = new double[4];
                for (int a = 0; a < n; a++)
                    for (int b = 0; b < n; b++)
                        nv[a] += cov[a, b] * v[b];
                double len = 0;
                for (int c = 0; c < n; c++)
                    len += nv[c] * nv[c];
                len = Math.Sqrt(len);
                if (len < 1e-9)
                    break;
                for (int c = 0; c < n; c++)
                    v[c] = nv[c] / len;
            }
            double l2 = 0;
            for (int c = 0; c < n; c++)
                l2 += v[c] * v[c];
            l2 = Math.Sqrt(l2);
            for (int c = 0; c < n; c++)
                v[c] = l2 > 1e-9 ? v[c] / l2 : 0;
            return v;
        }

        /// <summary>Least-squares endpoints for fixed indices (weights w / scale). False when the system is singular.</summary>
        private static bool RefitEndpoints(int[] px, int n, int[] idx, int[] weights, int scale, double[] e0, double[] e1)
        {
            double aa = 0, ab = 0, bb = 0;
            var ax = new double[4];
            var bx = new double[4];
            for (int i = 0; i < 16; i++)
            {
                double t = weights[idx[i]] / (double)scale, s = 1 - t;
                aa += s * s;
                ab += s * t;
                bb += t * t;
                for (int c = 0; c < n; c++)
                {
                    ax[c] += s * px[i * 4 + c];
                    bx[c] += t * px[i * 4 + c];
                }
            }
            double det = aa * bb - ab * ab;
            if (Math.Abs(det) < 1e-9)
                return false;
            for (int c = 0; c < n; c++)
            {
                e0[c] = Clamp((bb * ax[c] - ab * bx[c]) / det, 0, 255);
                e1[c] = Clamp((aa * bx[c] - ab * ax[c]) / det, 0, 255);
            }
            return true;
        }

        private static void Put(byte[] block, ref int bit, int value, int count)
        {
            for (int i = 0; i < count; i++, bit++)
            {
                if (((value >> i) & 1) != 0)
                    block[bit >> 3] |= (byte)(1 << (bit & 7));
            }
        }

        private static int Get(byte[] block, int bit, int count)
        {
            int v = 0;
            for (int i = 0; i < count; i++)
            {
                int b = bit + i;
                if ((block[b >> 3] & (1 << (b & 7))) != 0)
                    v |= 1 << i;
            }
            return v;
        }

        private static double Clamp(double v, double lo, double hi)
        {
            return v < lo ? lo : v > hi ? hi : v;
        }

        private static byte ToByte(double v)
        {
            return (byte)Math.Max(0, Math.Min(255, (int)Math.Round(v)));
        }
    }
}
