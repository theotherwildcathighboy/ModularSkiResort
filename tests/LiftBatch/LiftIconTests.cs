// Tests for the lift's notification icon rules (LiftLineIconRule) and the icon texture codec (IconTextureCodec). Where-tags
// `lift-icon/...`, LI-1..LI-8. Pinned: a station needs a line exactly while it has stub nodes and no outside lift edge at them; a loose
// lift end exactly at one lift edge; the show / clear command table; which vanilla electricity icon is stripped from which owner (the
// base station keeps its low-voltage icon and its own power warnings, nothing else survives); every encoder round-trips through its
// decoder within a quality floor and keeps fully clear pixels clear; mip chain sizes; the icon files and their locale keys.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using SkiResortMod.Systems;

namespace LiftBatch
{
    public static class LiftIconTests
    {
        private static Func<bool, string, string, bool> s_Check;

        public static void Run(Func<bool, string, string, bool> check)
        {
            s_Check = check;
            Li1StationNeedsLine();
            Li2LooseEnd();
            Li3Decide();
            Li4Strips();
            Li5MipChain();
            Li6EncodersRoundTrip();
            Li7Resize();
            Li8IconFilesAndText();
            s_Check = null;
        }

        private static bool Check(bool ok, string where, string what) => s_Check(ok, where, what);

        private static void Eq<T>(T expected, T actual, string where, string what)
        {
            s_Check(EqualityComparer<T>.Default.Equals(expected, actual), where, what + ": expected " + expected + ", got " + actual);
        }

        private static void Li1StationNeedsLine()
        {
            const string w = "lift-icon/LI-1";
            for (int stubs = 0; stubs <= 3; stubs++)
                for (int edges = -1; edges <= 3; edges++)
                    Eq(stubs > 0 && edges <= 0, LiftLineIconRule.StationNeedsLine(stubs, edges), w, $"stubs {stubs}, edges {edges}");
        }

        private static void Li2LooseEnd()
        {
            const string w = "lift-icon/LI-2";
            Eq(false, LiftLineIconRule.LooseEndNeedsLine(0), w, "no edge (a node about to go)");
            Eq(true, LiftLineIconRule.LooseEndNeedsLine(1), w, "one edge (a cut end)");
            Eq(false, LiftLineIconRule.LooseEndNeedsLine(2), w, "two edges (a tower)");
            Eq(false, LiftLineIconRule.LooseEndNeedsLine(3), w, "three edges");
        }

        private static void Li3Decide()
        {
            const string w = "lift-icon/LI-3";
            Eq(LineIconAction.Show, LiftLineIconRule.Decide(true, false), w, "needs, no icon");
            Eq(LineIconAction.None, LiftLineIconRule.Decide(true, true), w, "needs, has icon");
            Eq(LineIconAction.Clear, LiftLineIconRule.Decide(false, true), w, "has line, has icon");
            Eq(LineIconAction.None, LiftLineIconRule.Decide(false, false), w, "has line, no icon");
            Eq("show", LiftLineIconRule.Describe(LineIconAction.Show), w, "Describe show");
            Eq("clear", LiftLineIconRule.Describe(LineIconAction.Clear), w, "Describe clear");
            Eq("none", LiftLineIconRule.Describe(LineIconAction.None), w, "Describe none");
        }

        /// <summary>The whole table: the only kept cue is the base station's low-voltage icon; Other is never touched; the station-only
        /// cues (transformer, not enough production, no electricity, building bottleneck) go from both stations and are left alone on lift
        /// nodes and edges (vanilla never puts them there).</summary>
        private static void Li4Strips()
        {
            const string w = "lift-icon/LI-4";
            foreach (LiftIconOwner owner in Enum.GetValues(typeof(LiftIconOwner)))
                foreach (VanillaPowerIcon icon in Enum.GetValues(typeof(VanillaPowerIcon)))
                {
                    bool expected;
                    switch (icon)
                    {
                        case VanillaPowerIcon.Other:
                        case VanillaPowerIcon.LowVoltageNotConnected:
                            expected = false;
                            break;
                        case VanillaPowerIcon.NoElectricity:
                        case VanillaPowerIcon.BuildingBottleneck:
                        case VanillaPowerIcon.Transformer:
                        case VanillaPowerIcon.NotEnoughProduction:
                            expected = owner == LiftIconOwner.BaseStation || owner == LiftIconOwner.TopStation;
                            break;
                        default:
                            expected = true;
                            break;
                    }
                    Eq(expected, LiftLineIconRule.Strips(owner, icon), w, $"{owner} / {icon}");
                }
            // The rule the system is built on, stated directly.
            Check(!LiftLineIconRule.Strips(LiftIconOwner.BaseStation, VanillaPowerIcon.LowVoltageNotConnected), w,
                  "the base station keeps its low-voltage icon");
            Check(LiftLineIconRule.Strips(LiftIconOwner.BaseStation, VanillaPowerIcon.NoElectricity) &&
                  LiftLineIconRule.Strips(LiftIconOwner.BaseStation, VanillaPowerIcon.BuildingBottleneck), w,
                  "the base station's consumer warnings go (only the low-voltage icon stays)");
            Check(LiftLineIconRule.Strips(LiftIconOwner.BaseStation, VanillaPowerIcon.HighVoltageNotConnected) &&
                  LiftLineIconRule.Strips(LiftIconOwner.TopStation, VanillaPowerIcon.HighVoltageNotConnected) &&
                  LiftLineIconRule.Strips(LiftIconOwner.LiftNode, VanillaPowerIcon.HighVoltageNotConnected), w,
                  "the high-voltage icon goes everywhere");
        }

        private static void Li5MipChain()
        {
            const string w = "lift-icon/LI-5";
            Eq(1, IconTextureCodec.FullMipCount(1, 1), w, "1 x 1");
            Eq(9, IconTextureCodec.FullMipCount(256, 256), w, "256 x 256");
            Eq(10, IconTextureCodec.FullMipCount(512, 256), w, "512 x 256");
            Eq(256 * 256 * 4, IconTextureCodec.LevelBytes(IconTexFormat.RGBA32, 256, 256), w, "RGBA32 bytes");
            Eq(64 * 64, IconTextureCodec.LevelBytes(IconTexFormat.BC7, 64, 64), w, "BC7 bytes (16 per 4 x 4 block)");
            Eq(64 * 64, IconTextureCodec.LevelBytes(IconTexFormat.DXT5, 64, 64), w, "DXT5 bytes (16 per 4 x 4 block)");
            Eq(16, IconTextureCodec.LevelBytes(IconTexFormat.BC7, 2, 2), w, "a 2 x 2 BC7 level is one block");
            Eq(16, IconTextureCodec.LevelBytes(IconTexFormat.DXT5, 1, 1), w, "a 1 x 1 DXT5 level is one block");
            byte[] top = Pattern(64, 64);
            List<byte[]> chain = IconTextureCodec.MipChain(top, 64, 64, 7);
            Eq(7, chain.Count, w, "level count");
            int s = 64;
            for (int i = 0; i < chain.Count; i++, s = Math.Max(1, s >> 1))
                Eq(s * s * 4, chain[i].Length, w, $"level {i} bytes");
            // A uniform image stays uniform down the chain.
            byte[] flat = new byte[16 * 16 * 4];
            for (int i = 0; i < flat.Length; i += 4)
            {
                flat[i] = 255;
                flat[i + 1] = 184;
                flat[i + 2] = 14;
                flat[i + 3] = 255;
            }
            List<byte[]> flatChain = IconTextureCodec.MipChain(flat, 16, 16, 5);
            byte[] last = flatChain[flatChain.Count - 1];
            Check(last[0] == 255 && last[1] == 184 && last[2] == 14 && last[3] == 255, w,
                  $"flat yellow 1 x 1 mip: {last[0]},{last[1]},{last[2]},{last[3]}");
        }

        /// <summary>Every block encoder against its decoder: color PSNR over opaque pixels, alpha error, clear stays clear, sizes
        /// not a multiple of 4. The tiny images (6 and 2 px: whole 2-D color ramps inside one block, which one color line cannot hold)
        /// get a lower color floor; a real icon mip that small is one blended color.</summary>
        private static void Li6EncodersRoundTrip()
        {
            const string w = "lift-icon/LI-6";
            foreach (int size in new[] { 64, 6, 2, 1 })
            {
                byte[] src = Pattern(size, size);
                byte[] bc7 = IconTextureCodec.EncodeBc7(src, size, size);
                Eq(IconTextureCodec.LevelBytes(IconTexFormat.BC7, size, size), bc7.Length, w, $"BC7 {size} bytes");
                Quality(w, "BC7 " + size, src, IconTextureCodec.DecodeBc7(bc7, size, size), size >= 16 ? 38.0 : 24.0, 6);
                byte[] dxt = IconTextureCodec.EncodeDxt5(src, size, size);
                Eq(IconTextureCodec.LevelBytes(IconTexFormat.DXT5, size, size), dxt.Length, w, $"DXT5 {size} bytes");
                Quality(w, "DXT5 " + size, src, IconTextureCodec.DecodeDxt5(dxt, size, size), size >= 16 ? 28.0 : 24.0, 12);
                byte[] raw = IconTextureCodec.EncodeLevel(IconTexFormat.RGBA32, src, size, size);
                Check(raw.Length == src.Length && Same(raw, src), w, $"RGBA32 {size} is a copy");
                byte[] argb = IconTextureCodec.EncodeLevel(IconTexFormat.ARGB32, src, size, size);
                Check(argb.Length == src.Length && argb[0] == src[3] && argb[1] == src[0] && argb[2] == src[1] && argb[3] == src[2], w,
                      $"ARGB32 {size} channel order");
            }
            // A solid opaque color keeps alpha exactly 255 (it needs p-bit 1) and its color within 2 steps: the vanilla yellow.
            byte[] yellow = new byte[8 * 8 * 4];
            for (int i = 0; i < yellow.Length; i += 4)
            {
                yellow[i] = 255;
                yellow[i + 1] = 184;
                yellow[i + 2] = 14;
                yellow[i + 3] = 255;
            }
            byte[] y7 = IconTextureCodec.DecodeBc7(IconTextureCodec.EncodeBc7(yellow, 8, 8), 8, 8);
            Check(Math.Abs(y7[0] - 255) <= 2 && Math.Abs(y7[1] - 184) <= 2 && Math.Abs(y7[2] - 14) <= 2 && y7[3] == 255, w,
                  $"BC7 solid yellow: {y7[0]},{y7[1]},{y7[2]},{y7[3]}");
        }

        private static void Li7Resize()
        {
            const string w = "lift-icon/LI-7";
            byte[] src = Pattern(512, 512);
            byte[] r = IconTextureCodec.Resize(src, 512, 512, 128, 128);
            Eq(128 * 128 * 4, r.Length, w, "resized bytes");
            // A clear pixel region stays clear, an opaque one stays opaque (alpha-weighted box filter: no dark fringe).
            Check(r[3] == 0, w, $"corner alpha after resize: {r[3]}");
            int c = (64 * 128 + 64) * 4;
            Check(r[c + 3] == 255, w, $"center alpha after resize: {r[c + 3]}");
            byte[] up = IconTextureCodec.Resize(Pattern(64, 64), 64, 64, 256, 256);
            Eq(256 * 256 * 4, up.Length, w, "upsized bytes");
        }

        /// <summary>The shipped files: the PNG is a square power-of-two RGBA image, the SVG exists, the csproj copies both to
        /// SkiResortMod-icons, and LocaleEN has the title and description under the prefab's name, without an em dash.</summary>
        private static void Li8IconFilesAndText()
        {
            const string w = "lift-icon/LI-8";
            string asset = CatalogTests.RepoFile("mod/SkiResortMod/Systems/LiftLineIconAsset.cs");
            string loc = CatalogTests.RepoFile("mod/SkiResortMod/LocaleEN.cs");
            string proj = CatalogTests.RepoFile("mod/SkiResortMod/SkiResortMod.csproj");
            if (!Check(asset != null && loc != null && proj != null, w, "LiftLineIconAsset.cs, LocaleEN.cs or SkiResortMod.csproj not found"))
                return;
            string assetText = File.ReadAllText(asset);
            Match name = Regex.Match(assetText, "const string PrefabName = \"([^\"]+)\"");
            Match folder = Regex.Match(assetText, "const string IconFolder = \"([^\"]+)\"");
            Match png = Regex.Match(assetText, "const string PngFile = \"([^\"]+)\"");
            if (!Check(name.Success && folder.Success && png.Success, w, "PrefabName / IconFolder / PngFile constants not found"))
                return;
            string prefab = name.Groups[1].Value;
            Eq(prefab + ".png", png.Groups[1].Value, w, "the PNG is named after the prefab");
            Check(assetText.Contains("\"/" + prefab + ".svg\""), w, "the UI icon is the prefab's SVG");

            string pngPath = CatalogTests.RepoFile("mod/SkiResortMod/Icons/" + prefab + ".png");
            string svgPath = CatalogTests.RepoFile("mod/SkiResortMod/Icons/" + prefab + ".svg");
            Check(svgPath != null, w, prefab + ".svg missing in mod/SkiResortMod/Icons");
            if (Check(pngPath != null, w, prefab + ".png missing in mod/SkiResortMod/Icons"))
            {
                byte[] b = File.ReadAllBytes(pngPath);
                bool header = b.Length > 33 && b[0] == 0x89 && b[1] == (byte)'P' && b[12] == (byte)'I' && b[13] == (byte)'H';
                if (Check(header, w, "PNG signature / IHDR"))
                {
                    int width = (b[16] << 24) | (b[17] << 16) | (b[18] << 8) | b[19];
                    int height = (b[20] << 24) | (b[21] << 16) | (b[22] << 8) | b[23];
                    Check(width == height && width >= 256 && (width & (width - 1)) == 0, w, $"PNG {width} x {height}: square, power of two, >= 256");
                    Eq(8, (int)b[24], w, "PNG bit depth");
                    Eq(6, (int)b[25], w, "PNG color type (RGBA)");
                }
            }

            string projText = File.ReadAllText(proj);
            Check(projText.Contains("Link=\"" + folder.Groups[1].Value + "\\%(Filename)%(Extension)\"") &&
                  projText.Contains("Icons\\*.svg;Icons\\*.png"), w, "the csproj copies Icons\\*.svg and *.png to " + folder.Groups[1].Value);

            string locText = File.ReadAllText(loc);
            foreach (string key in new[] { "Notifications.TITLE[" + prefab + "]", "Notifications.DESCRIPTION[" + prefab + "]" })
            {
                Match m = Regex.Match(locText, "\\{ \"" + Regex.Escape(key) + "\", \"((?:[^\"\\\\]|\\\\.)*)\" \\}");
                if (!Check(m.Success, w, key + " missing in LocaleEN.cs"))
                    continue;
                string text = m.Groups[1].Value;
                Check(text.Length > 0 && text.IndexOf('\u2014') < 0, w, key + ": empty or has an em dash");
            }
        }

        /// <summary>A test image: a clear border, an opaque diamond with color ramps and hard edges inside.</summary>
        private static byte[] Pattern(int wd, int ht)
        {
            var px = new byte[wd * ht * 4];
            for (int y = 0; y < ht; y++)
                for (int x = 0; x < wd; x++)
                {
                    int i = (y * wd + x) * 4;
                    float u = (x + 0.5f) / wd - 0.5f, v = (y + 0.5f) / ht - 0.5f;
                    bool inside = Math.Abs(u) + Math.Abs(v) < 0.45f || wd <= 2;
                    if (!inside)
                        continue;
                    bool bar = Math.Abs(u) < 0.06f;
                    px[i] = (byte)(bar ? 120 : 255);
                    px[i + 1] = (byte)(bar ? 120 : 140 + (int)(80 * (v + 0.5f)));
                    px[i + 2] = (byte)(bar ? 120 : (int)(60 * (u + 0.5f)));
                    px[i + 3] = 255;
                }
            return px;
        }

        private static void Quality(string w, string what, byte[] src, byte[] dec, double minPsnr, int maxAlphaError)
        {
            if (!Check(dec.Length == src.Length, w, $"{what}: decoded {dec.Length} bytes, expected {src.Length}"))
                return;
            double se = 0;
            int n = 0, alphaErr = 0;
            bool clearKept = true;
            for (int i = 0; i < src.Length; i += 4)
            {
                alphaErr = Math.Max(alphaErr, Math.Abs(src[i + 3] - dec[i + 3]));
                if (src[i + 3] == 0 && dec[i + 3] != 0)
                    clearKept = false;
                if (src[i + 3] != 255)
                    continue;
                for (int c = 0; c < 3; c++)
                {
                    double d = src[i + c] - dec[i + c];
                    se += d * d;
                }
                n += 3;
            }
            double psnr = n == 0 || se == 0 ? 99.0 : 10.0 * Math.Log10(255.0 * 255.0 / (se / n));
            Check(psnr >= minPsnr, w, $"{what}: color PSNR {psnr:F1} dB < {minPsnr}");
            Check(alphaErr <= maxAlphaError, w, $"{what}: alpha error {alphaErr} > {maxAlphaError}");
            Check(clearKept, w, $"{what}: a fully clear pixel came back visible");
        }

        private static bool Same(byte[] a, byte[] b)
        {
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i])
                    return false;
            return true;
        }
    }
}
