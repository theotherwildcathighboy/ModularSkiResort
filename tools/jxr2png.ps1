<#
.SYNOPSIS
    Convert HDR .jxr / .wdp screenshots to SDR .png with proper tone mapping.

.DESCRIPTION
    Windows HDR screenshots are JPEG XR in scRGB: LINEAR light, floating point, where 1.0 is
    SDR reference white (80 nits) and highlights go WELL above 1.0.

    Naive converters do two things wrong at once:
      1. clamp everything above 1.0, destroying all highlight detail, and
      2. treat linear values as if they were already gamma-encoded.
    Together those produce the classic washed-out, milky result. The image isn't damaged -
    it's being displayed with the wrong transfer function.

    This script decodes with the JPEG XR codec built into Windows (no install required),
    applies a filmic tone curve, then encodes sRGB properly.

.PARAMETER Path
    A .jxr/.wdp file, or a folder to convert in bulk.

.PARAMETER Exposure
    Linear multiplier applied before tone mapping. Ignored when -Auto is set.

.PARAMETER Auto
    Pick exposure automatically so the 99.5th-percentile highlight lands just below clipping.
    Usually the right choice - start here.

.PARAMETER Mode
    aces     filmic, keeps highlight rolloff, best default
    reinhard gentler, flatter contrast
    clip     no tone mapping, just linear->sRGB. Shows what you were getting before.

.EXAMPLE
    .\jxr2png.ps1 -Path "C:\shots\ski.jxr" -Auto
    .\jxr2png.ps1 -Path "C:\shots" -Auto -Mode aces
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $Path,
    [double] $Exposure = 1.0,
    [switch] $Auto,
    [ValidateSet('aces', 'reinhard', 'clip')] [string] $Mode = 'aces',
    [string] $OutDir
)

Add-Type -AssemblyName PresentationCore, WindowsBase

$cs = @'
using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

public static class JxrTool
{
    // Decode any WIC-supported HDR image to linear float RGBA.
    public static float[] Decode(string path, out int w, out int h)
    {
        BitmapFrame frame;
        using (var fs = File.OpenRead(path))
        {
            var dec = BitmapDecoder.Create(fs, BitmapCreateOptions.PreservePixelFormat,
                                           BitmapCacheOption.OnLoad);
            frame = dec.Frames[0];
        }
        var conv = new FormatConvertedBitmap(frame, PixelFormats.Rgba128Float, null, 0);
        w = conv.PixelWidth;
        h = conv.PixelHeight;
        var px = new float[w * h * 4];
        conv.CopyPixels(px, w * 16, 0);
        return px;
    }

    // Luminance value at a given percentile - used for auto exposure.
    public static double Percentile(float[] px, double pct)
    {
        int n = px.Length / 4;
        var lum = new float[n];
        for (int i = 0; i < n; i++)
        {
            float r = px[i * 4], g = px[i * 4 + 1], b = px[i * 4 + 2];
            lum[i] = 0.2126f * r + 0.7152f * g + 0.0722f * b;
        }
        Array.Sort(lum);
        int idx = (int)(pct * (n - 1));
        if (idx < 0) idx = 0;
        if (idx >= n) idx = n - 1;
        return lum[idx];
    }

    public static double MaxLum(float[] px)
    {
        double m = 0;
        int n = px.Length / 4;
        for (int i = 0; i < n; i++)
        {
            double l = 0.2126 * px[i * 4] + 0.7152 * px[i * 4 + 1] + 0.0722 * px[i * 4 + 2];
            if (l > m) m = l;
        }
        return m;
    }

    static double Aces(double x)
    {
        // Narkowicz ACES filmic approximation
        const double a = 2.51, b = 0.03, c = 2.43, d = 0.59, e = 0.14;
        double v = (x * (a * x + b)) / (x * (c * x + d) + e);
        return v < 0 ? 0 : (v > 1 ? 1 : v);
    }

    static double ToneMap(double x, string mode)
    {
        if (x < 0) x = 0;
        switch (mode)
        {
            case "aces":     return Aces(x);
            case "reinhard": return x / (1.0 + x);
            default:         return x > 1 ? 1 : x;   // clip
        }
    }

    static double LinearToSrgb(double c)
    {
        if (c <= 0.0) return 0.0;
        if (c > 1.0) c = 1.0;
        return c <= 0.0031308 ? c * 12.92 : 1.055 * Math.Pow(c, 1.0 / 2.4) - 0.055;
    }

    public static void Write(string outPath, float[] px, int w, int h,
                             double exposure, string mode)
    {
        var outPx = new byte[w * h * 4];
        int n = w * h;
        for (int i = 0; i < n; i++)
        {
            for (int ch = 0; ch < 3; ch++)
            {
                double v = px[i * 4 + ch] * exposure;
                v = ToneMap(v, mode);
                v = LinearToSrgb(v);
                int b = (int)Math.Round(v * 255.0);
                outPx[i * 4 + ch] = (byte)(b < 0 ? 0 : (b > 255 ? 255 : b));
            }
            outPx[i * 4 + 3] = 255;
        }

        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, outPx, w * 4);
        // channel order: our buffer is RGBA, so swap into BGRA
        var swapped = new byte[outPx.Length];
        for (int i = 0; i < n; i++)
        {
            swapped[i * 4 + 0] = outPx[i * 4 + 2];
            swapped[i * 4 + 1] = outPx[i * 4 + 1];
            swapped[i * 4 + 2] = outPx[i * 4 + 0];
            swapped[i * 4 + 3] = 255;
        }
        bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, swapped, w * 4);

        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using (var fs = File.Create(outPath)) { enc.Save(fs); }
    }
}
'@

Add-Type -TypeDefinition $cs -ReferencedAssemblies PresentationCore, WindowsBase, System.Xaml

# ---- gather input files -------------------------------------------------------------
$files = @()
if (Test-Path $Path -PathType Container) {
    $files = Get-ChildItem $Path -Include *.jxr, *.wdp -Recurse -File
} elseif (Test-Path $Path) {
    $files = @(Get-Item $Path)
} else {
    Write-Error "Not found: $Path"; exit 1
}

if (-not $files) { Write-Warning "No .jxr/.wdp files found under $Path"; exit 0 }

foreach ($f in $files) {
    $target = if ($OutDir) { $OutDir } else { $f.DirectoryName }
    if (-not (Test-Path $target)) { New-Item -ItemType Directory -Force $target | Out-Null }
    $out = Join-Path $target ($f.BaseName + '.png')

    $w = 0; $h = 0
    $px = [JxrTool]::Decode($f.FullName, [ref]$w, [ref]$h)

    $maxL = [JxrTool]::MaxLum($px)
    $p995 = [JxrTool]::Percentile($px, 0.995)
    $p50 = [JxrTool]::Percentile($px, 0.50)

    $exp = $Exposure
    if ($Auto) {
        # Gray-world: put the MEDIAN at classic 18% mid-gray.
        #
        # Anchoring on the 99.5th percentile instead looks correct in theory but comes out far
        # too dark in practice - in a game screenshot that percentile is bright UI chrome or a
        # specular hit, so normalizing to it drags the whole scene down. The median tracks what
        # the image is actually *of*.
        if ($p50 -gt 0.0001) { $exp = 0.18 / $p50 } else { $exp = 1.0 }

        # Guard: don't push highlights so far up that even ACES can't roll them off.
        if (($p995 * $exp) -gt 12.0) { $exp = 12.0 / $p995 }
    }

    [JxrTool]::Write($out, $px, $w, $h, $exp, $Mode)

    "{0}  ->  {1}" -f $f.Name, (Split-Path $out -Leaf)
    "    {0}x{1}  peak {2:N2}  median {3:N3}  p99.5 {4:N2}  exposure {5:N3}  mode {6}" -f `
        $w, $h, $maxL, $p50, $p995, $exp, $Mode
}
