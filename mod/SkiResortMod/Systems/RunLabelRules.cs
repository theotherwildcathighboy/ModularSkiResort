// Pure part of the run label rules (SkiRunLabelSystem): zoom fade, band colors, text clean-up and size / distance rules of the
// run-name labels.
//   FadeAlpha  0 below ZoomHideBelowM and above ZoomHideAboveM (and for a zoom that is not a positive finite number), linear ramps
//              between. Reads the same camera zoom as the piste map (IGameCameraController.zoom, PisteMapRules.WidthScale).
//   AlphaStep  the fade quantized to AlphaSteps (0 = hidden). Vertex alpha is rewritten only when the step moves.
//   BandRgb    the piste-map fill colors of SkiPisteMapSystem.s_Fill by catalog RunRow.Band (-2 cannot be rated .. 4 double black).
//   CleanText  removes TextMeshPro mark-up characters ('<', '>') and control characters, so a run named "<b>x" cannot become mark-up.
// Pure file: `using System;` and Unity.Mathematics only (the ResortBatch host links it).

using System;
using System.Text;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public static class RunLabelRules
    {
        // Camera zoom in meters (IGameCameraController.zoom)
        public const float ZoomHideBelowM = 100f;   // closer than this: the run itself is the thing to look at
        public const float ZoomFullFromM = 250f;
        public const float ZoomFullToM = 1500f;
        public const float ZoomHideAboveM = 3000f;  // the label's screen size falls with sqrt(distance): unreadable beyond this

        public const int AlphaSteps = 8;
        public const int MaxChars = 32;
        public const float MaxWidthFraction = 1.0f;
        public const float CullBaseM = 500f;
        public const float CullPerZoom = 2.5f;

        // The text is rigid and the ground is not, so it rides above the highest of this grid of samples under it.
        public const int ClearAlong = 7;
        public const int ClearAcross = 3;

        public const float AxisMinRatio = 1.5f;     // outline spread along / across: below this a run has no clear direction
        public const float FlipMargin = 0.1f;       // the text turns over only this far past square to the camera, so it does not flicker there
        public const float MaxSlope = 1.2f;         // rise / run of the text's plane: on a cliff it stays readable from above
        public const float CurvedMinDegrees = 30f;  // local direction this far off the overall axis: the run counts as curved at its label
        public const float CurvedWindowM = 40f;     // edges this near the label give the local direction

        /// <summary>Sample i of count as a fraction of the half extent: -1 at one edge, +1 at the other, 0 for one sample.</summary>
        public static float ClearFraction(int i, int count)
        {
            return count <= 1 ? 0f : -1f + 2f * i / (count - 1);
        }

        /// <summary>The run's long direction: principal axis of the outline, weighted by edge length so node spacing does not bias it.
        /// False for an outline with no clear direction (ratio under AxisMinRatio), under 3 nodes, or not finite.</summary>
        public static bool LongAxis(float2[] outlineXZ, out float2 axis)
        {
            axis = new float2(1f, 0f);
            if (outlineXZ == null || outlineXZ.Length < 3)
                return false;
            double total = 0, mx = 0, mz = 0;
            int n = outlineXZ.Length;
            for (int i = 0; i < n; i++)
            {
                float2 a = outlineXZ[i], b = outlineXZ[(i + 1) % n];
                if (!math.all(math.isfinite(a)) || !math.all(math.isfinite(b)))
                    return false;
                double w = math.distance(a, b);
                total += w;
                mx += w * (a.x + b.x) * 0.5;
                mz += w * (a.y + b.y) * 0.5;
            }
            if (!(total > 0))
                return false;
            mx /= total;
            mz /= total;
            double cxx = 0, cxz = 0, czz = 0;
            for (int i = 0; i < n; i++)
            {
                float2 a = outlineXZ[i], b = outlineXZ[(i + 1) % n];
                double w = math.distance(a, b);
                double ax = a.x - mx, az = a.y - mz, bx = b.x - mx, bz = b.y - mz;
                // Second moments of a straight edge from its two ends.
                cxx += w * (ax * ax + ax * bx + bx * bx) / 3.0;
                czz += w * (az * az + az * bz + bz * bz) / 3.0;
                cxz += w * (2.0 * ax * az + ax * bz + bx * az + 2.0 * bx * bz) / 6.0;
            }
            double spread = Math.Sqrt((cxx - czz) * (cxx - czz) + 4.0 * cxz * cxz);
            double major = (cxx + czz + spread) * 0.5, minor = (cxx + czz - spread) * 0.5;
            if (!(major > 0) || major < AxisMinRatio * AxisMinRatio * Math.Max(minor, 0.0))
                return false;
            double angle = 0.5 * Math.Atan2(2.0 * cxz, cxx - czz);
            axis = new float2((float)Math.Cos(angle), (float)Math.Sin(angle));
            return true;
        }

        /// <summary>A curved run: its overall axis does not describe the ground under the label, or its center lies off the run. True gives the
        /// spot between the two sides nearest the center and the run's direction there. False for a straight run (the local direction within
        /// CurvedMinDegrees of a clear overall axis, center on the run), a run about as wide as it is long (width over half its extent), a bad outline.</summary>
        public static bool CurvedAnchor(float2[] outlineXZ, float2 center, bool hasAxis, float2 axis, out float2 anchor, out float2 local)
        {
            anchor = center;
            local = axis;
            if (outlineXZ == null || outlineXZ.Length < 4 || !math.all(math.isfinite(center)))
                return false;
            int n = outlineXZ.Length;
            double doubledArea = 0;
            int nearest = -1;
            float best = float.PositiveInfinity;
            float2 onEdge = center;
            for (int i = 0; i < n; i++)
            {
                float2 a = outlineXZ[i], b = outlineXZ[(i + 1) % n];
                if (!math.all(math.isfinite(a)) || !math.all(math.isfinite(b)))
                    return false;
                doubledArea += a.x * b.y - b.x * a.y;
                float2 p = NearestOnEdge(center, a, b);
                float d = math.lengthsq(center - p);
                if (d < best && math.lengthsq(b - a) > 0f)
                {
                    best = d;
                    nearest = i;
                    onEdge = p;
                }
            }
            if (nearest < 0 || doubledArea == 0)
                return false;
            float2 along = math.normalize(outlineXZ[(nearest + 1) % n] - outlineXZ[nearest]);
            float2 inward = doubledArea > 0 ? new float2(-along.y, along.x) : new float2(along.y, -along.x);
            // Across the run from the nearest edge to the first edge on the other side.
            float width = float.PositiveInfinity;
            for (int i = 0; i < n; i++)
            {
                if (i == nearest)
                    continue;
                float2 a = outlineXZ[i], e = outlineXZ[(i + 1) % n] - a;
                float denom = inward.x * e.y - inward.y * e.x;
                if (denom == 0f)
                    continue;
                float2 w = a - onEdge;
                float t = (w.x * e.y - w.y * e.x) / denom;
                float u = (w.x * inward.y - w.y * inward.x) / denom;
                if (t > 0.01f && t < width && u >= 0f && u <= 1f)
                    width = t;
            }
            if (float.IsInfinity(width) || width > 0.5f * Extent(outlineXZ))
                return false;
            anchor = onEdge + inward * (0.5f * width);
            // The run's direction here: the edges around the spot, all turned the same way as the nearest one, by length.
            float2 sum = float2.zero;
            float window2 = CurvedWindowM * CurvedWindowM;
            for (int i = 0; i < n; i++)
            {
                float2 a = outlineXZ[i], b = outlineXZ[(i + 1) % n];
                if (math.lengthsq(anchor - NearestOnEdge(anchor, a, b)) > window2)
                    continue;
                float2 e = b - a;
                sum += math.dot(e, along) < 0f ? -e : e;
            }
            local = math.lengthsq(sum) > 0f ? math.normalize(sum) : along;
            bool centerOn = math.lengthsq(center - anchor) <= 0.25f * width * width;
            return !hasAxis || !centerOn || math.abs(math.dot(axis, local)) < math.cos(math.radians(CurvedMinDegrees));
        }

        private static float2 NearestOnEdge(float2 p, float2 a, float2 b)
        {
            float2 ab = b - a;
            float len2 = math.lengthsq(ab);
            return len2 > 0f ? a + ab * math.saturate(math.dot(p - a, ab) / len2) : a;
        }

        /// <summary>Whether the text runs against the axis, so it never reads upside down. Holds its state within FlipMargin of square to the camera.</summary>
        public static bool ReadsFlipped(float2 axis, float2 cameraRight, bool flipped)
        {
            float d = math.dot(axis, cameraRight);
            return flipped ? d < FlipMargin : d < -FlipMargin;
        }

        /// <summary>The text's plane from its direction on the map and the ground's rise along and across it. x runs along the text, y up the
        /// lines of text, normal away from the ground (x = (1,0,0), flat ground: y = (0,0,1), normal = (0,1,0)).</summary>
        public static void TextBasis(float2 direction, float slopeAlong, float slopeAcross, out float3 x, out float3 y, out float3 normal)
        {
            float along = math.clamp(slopeAlong, -MaxSlope, MaxSlope);
            float across = math.clamp(slopeAcross, -MaxSlope, MaxSlope);
            x = math.normalize(new float3(direction.x, along, direction.y));
            float3 side = new float3(-direction.y, across, direction.x);
            normal = math.normalize(math.cross(side, x));
            y = math.cross(x, normal);
        }

        /// <summary>Fade 0..1 by camera zoom.</summary>
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
                case -2: r = 220; g = 150; b = 60; return;
                case 0: r = 170; g = 180; b = 190; return;
                case 1: r = 76; g = 175; b = 80; return;
                case 2: r = 52; g = 120; b = 220; return;
                case 3: r = 14; g = 14; b = 18; return;
                case 4: r = 0; g = 0; b = 0; return;
                default: r = 255; g = 255; b = 255; return;
            }
        }

        /// <summary>Null / empty / all-removed gives "".</summary>
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

        public static float FitScale(float scale, float textWidthLocal, float extentM)
        {
            if (!(scale > 0f) || !(textWidthLocal > 0f) || !(extentM > 0f))
                return scale;
            float cap = MaxWidthFraction * extentM / textWidthLocal;
            return cap < scale ? cap : scale;
        }

        public static float CullRadius(float zoom)
        {
            return CullBaseM + CullPerZoom * (zoom > 0f ? zoom : 0f);
        }

        public static bool InRange(float dx, float dz, float zoom)
        {
            float r = CullRadius(zoom);
            return dx * dx + dz * dz <= r * r;
        }
    }
}
