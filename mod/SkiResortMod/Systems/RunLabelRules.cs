// RunLabelRules — the PURE part of SkiRunLabelSystem (ticket T37, 2026-10-16; docs/research/field/2026-10-15/build/
// T37-run-labels.md): the zoom fade, the band colors, the text clean-up and the size / distance rules of the run-name
// labels drawn on the hill. Tested by tests/ResortBatch RunLabelTests (literal oracles).
//
// RULES:
//   FadeAlpha   0 below ZoomHideBelowM and above ZoomHideAboveM (and for a zoom that is not a positive finite number: no
//               camera, a controller being torn down); linear 0 → 1 over ZoomHideBelowM..ZoomFullFromM, 1 up to
//               ZoomFullToM, linear 1 → 0 over ZoomFullToM..ZoomHideAboveM. The same camera zoom the piste map reads
//               (IGameCameraController.zoom, PisteMapRules.WidthScale), so "labels visible" and "outline widened for
//               the distance" describe the same view.
//   AlphaStep   the fade quantized to AlphaSteps (0 = hidden). The label meshes' vertex alpha is rewritten only when the
//               step moves (at most AlphaSteps times per zoom sweep), never per frame.
//   BandRgb     the piste-map fill colors of SkiPisteMapSystem.s_Fill (the hub's band colors), by catalog RunRow.Band:
//               -2 cannot be rated, -1 pending, 0 flat, 1 green, 2 blue, 3 black, 4 double black.
//   CleanText   TextMeshPro mark-up characters out ('<', '>'), control characters out, runs of spaces collapsed, trimmed,
//               at most MaxChars (cut + "..."): a run named "<b>x" must not turn the label into mark-up.
//   Extent      the larger side of the outline's x/z bounding box (m); 0 for no usable outline.
//   FitScale    the label scale capped so the text is not wider than MaxWidthFraction x the run's extent
//               (textWidth in the label's local units, i.e. before the scale); a degenerate input returns the scale.
//   InRange     squared-distance cull: a label further from the camera pivot than CullRadius(zoom) is not submitted.
//
// PURE FILE: `using System;` and Unity.Mathematics only (the ResortBatch host links it with UnityMathematics.NoDeps).

using System;
using System.Text;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public static class RunLabelRules
    {
        // Camera zoom (m, IGameCameraController.zoom) — field-tunable.
        public const float ZoomHideBelowM = 100f;   // closer than this: the run itself is the thing to look at
        public const float ZoomFullFromM = 250f;
        public const float ZoomFullToM = 1500f;
        public const float ZoomHideAboveM = 3000f;  // the label's screen size falls with sqrt(distance): unreadable beyond this

        public const int AlphaSteps = 8;
        public const int MaxChars = 32;
        public const float MaxWidthFraction = 1.0f;
        public const float CullBaseM = 500f;
        public const float CullPerZoom = 2.5f;

        /// <summary>Header RULES: the fade by camera zoom, 0..1.</summary>
        public static float FadeAlpha(float zoom)
        {
            if (!(zoom > 0f) || float.IsInfinity(zoom))
                return 0f;
            if (zoom <= ZoomHideBelowM || zoom >= ZoomHideAboveM)
                return 0f;
            if (zoom < ZoomFullFromM)
                return (zoom - ZoomHideBelowM) / (ZoomFullFromM - ZoomHideBelowM);
            if (zoom <= ZoomFullToM)
                return 1f;
            return (ZoomHideAboveM - zoom) / (ZoomHideAboveM - ZoomFullToM);
        }

        /// <summary>0..AlphaSteps; 0 = hidden. Any non-zero alpha gets at least step 1.</summary>
        public static int AlphaStep(float alpha)
        {
            if (!(alpha > 0f))
                return 0;
            if (alpha >= 1f)
                return AlphaSteps;
            int s = (int)Math.Round(alpha * AlphaSteps);
            return s < 1 ? 1 : s;
        }

        /// <summary>The vertex alpha byte of a step (255 at AlphaSteps, 0 at 0).</summary>
        public static byte AlphaByte(int step)
        {
            if (step <= 0)
                return 0;
            if (step >= AlphaSteps)
                return 255;
            return (byte)Math.Round(255.0 * step / AlphaSteps);
        }

        /// <summary>The piste map's fill color of a catalog band (SkiPisteMapSystem.s_Fill, alpha dropped).</summary>
        public static void BandRgb(int band, out byte r, out byte g, out byte b)
        {
            switch (band)
            {
                case -2: r = 220; g = 150; b = 60; return;    // cannot be rated
                case 0: r = 170; g = 180; b = 190; return;    // flat
                case 1: r = 76; g = 175; b = 80; return;      // green
                case 2: r = 52; g = 120; b = 220; return;     // blue
                case 3: r = 14; g = 14; b = 18; return;       // black
                case 4: r = 0; g = 0; b = 0; return;          // double black
                default: r = 255; g = 255; b = 255; return;   // pending (-1) and anything else
            }
        }

        /// <summary>Header RULES CleanText. Null / empty / all-removed → "".</summary>
        public static string CleanText(string s)
        {
            if (string.IsNullOrEmpty(s))
                return "";
            var sb = new StringBuilder(s.Length);
            bool lastSpace = true;   // trims the start
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '<' || c == '>')
                    continue;
                if (char.IsControl(c) || char.IsWhiteSpace(c))
                {
                    if (!lastSpace)
                        sb.Append(' ');
                    lastSpace = true;
                    continue;
                }
                sb.Append(c);
                lastSpace = false;
            }
            while (sb.Length > 0 && sb[sb.Length - 1] == ' ')
                sb.Length--;
            if (sb.Length > MaxChars)
            {
                sb.Length = MaxChars - 3;
                while (sb.Length > 0 && sb[sb.Length - 1] == ' ')
                    sb.Length--;
                sb.Append("...");
            }
            return sb.ToString();
        }

        /// <summary>The larger side of the x/z bounding box of the outline (m); 0 for null / empty / non-finite.</summary>
        public static float Extent(float2[] outlineXZ)
        {
            if (outlineXZ == null || outlineXZ.Length == 0)
                return 0f;
            float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
            for (int i = 0; i < outlineXZ.Length; i++)
            {
                float2 p = outlineXZ[i];
                if (float.IsNaN(p.x) || float.IsNaN(p.y) || float.IsInfinity(p.x) || float.IsInfinity(p.y))
                    return 0f;
                if (p.x < minX) minX = p.x;
                if (p.x > maxX) maxX = p.x;
                if (p.y < minZ) minZ = p.y;
                if (p.y > maxZ) maxZ = p.y;
            }
            float w = maxX - minX, h = maxZ - minZ;
            return w > h ? w : h;
        }

        /// <summary>Header RULES FitScale.</summary>
        public static float FitScale(float scale, float textWidthLocal, float extentM)
        {
            if (!(scale > 0f) || !(textWidthLocal > 0f) || !(extentM > 0f))
                return scale;
            float cap = MaxWidthFraction * extentM / textWidthLocal;
            return cap < scale ? cap : scale;
        }

        /// <summary>The label cull radius (m) at a camera zoom.</summary>
        public static float CullRadius(float zoom)
        {
            return CullBaseM + CullPerZoom * (zoom > 0f ? zoom : 0f);
        }

        /// <summary>True when the squared xz distance (dx, dz) to the camera pivot is within CullRadius(zoom).</summary>
        public static bool InRange(float dx, float dz, float zoom)
        {
            float r = CullRadius(zoom);
            return dx * dx + dz * dz <= r * r;
        }
    }
}
