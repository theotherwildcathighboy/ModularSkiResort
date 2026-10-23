// Pure rules of the piste-map overlay: outline style by band, the dashed test for natural runs, the zoom width scale, and
// cutting a run's closed node ring into the straight pieces SkiPisteMapSystem caches and draws.
// Natural runs draw nothing on the ground, so their outline is dashed to read as "a surface, not a groomed piste".
// Dashes are cut on the CPU: OverlayRenderSystem.Buffer.DrawDashedLine (OverlayRenderSystem.cs:218-221) restarts its pattern on
// every edge, so an outline of short edges would read solid. A bad dash or gap falls back to SolidRing.
// WidthScale: a projected line keeps its width in meters, so a far camera needs wider lines to see them.
// Both ring cutters append to the caller's lists and stop at maxPieces (return value == maxPieces means the cap was reached).
// Pure file: only System, System.Collections.Generic and Unity.Mathematics (the ResortBatch host links it with
// UnityMathematics.NoDeps).

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    /// <summary>Outline style by difficulty (the hub's band colors, hub.module.scss .band*).</summary>
    public enum PisteStyle : byte { Pending = 0, Unrateable = 1, Flat = 2, Green = 3, Blue = 4, Black = 5, DoubleBlack = 6 }

    /// <summary>The overlay's pure rules.</summary>
    public static class PisteMapRules
    {
        public const float RefZoomM = 750f;      // width x1 up to this camera zoom (m)
        public const float MaxWidthScale = 6f;   // capped at x6
        public const float MinPieceM = 0.05f;    // vanilla drops pieces under 0.01 m
        public const int KindNatural = 3;        // RunKind.Natural

        /// <summary>Band -2 Unrateable, -1 Pending, 0..4 Flat..DoubleBlack, anything else Pending.</summary>
        public static PisteStyle StyleOf(int band)
        {
            switch (band)
            {
                case -2: return PisteStyle.Unrateable;
                case 0: return PisteStyle.Flat;
                case 1: return PisteStyle.Green;
                case 2: return PisteStyle.Blue;
                case 3: return PisteStyle.Black;
                case 4: return PisteStyle.DoubleBlack;
                default: return PisteStyle.Pending;
            }
        }

        public static bool IsDashed(int kind) => kind == KindNatural;

        /// <summary>!(zoom &gt; 0) gives 1; else clamp(zoom / RefZoomM, 1, MaxWidthScale).</summary>
        public static float WidthScale(float zoom)
        {
            if (!(zoom > 0f))
                return 1f;
            float s = zoom / RefZoomM;
            if (!(s > 1f))
                return 1f;
            return s < MaxWidthScale ? s : MaxWidthScale;
        }

        /// <summary>Every edge of the closed ring (closing edge included) with finite ends and xz length &gt;= MinPieceM, appended as
        /// one piece. Returns the number appended.</summary>
        public static int SolidRing(List<float3> ring, int maxPieces, List<float3> outA, List<float3> outB)
        {
            int n = ring != null ? ring.Count : 0;
            if (n < 2 || maxPieces <= 0)
                return 0;
            int added = 0;
            for (int i = 0; i < n; i++)
            {
                float3 a = ring[i], b = ring[(i + 1) % n];
                if (!Finite(a) || !Finite(b) || !(LengthXZ(a, b) >= MinPieceM))
                    continue;
                outA.Add(a);
                outB.Add(b);
                if (++added >= maxPieces)
                    return maxPieces;
            }
            return added;
        }

        /// <summary>Walks the closed ring's xz perimeter from node 0, alternating dashM on and gapM off with the phase carried across
        /// nodes (double), and appends each "on" piece clipped to its edge. Edges with a non-finite end are skipped and add no length.
        /// A non-positive dash, negative gap or non-finite value falls back to SolidRing. Stops at maxPieces.</summary>
        public static int DashRing(List<float3> ring, float dashM, float gapM, int maxPieces, List<float3> outA, List<float3> outB)
        {
            if (!(dashM > 0f) || !(gapM >= 0f) || float.IsInfinity(dashM) || float.IsInfinity(gapM))
                return SolidRing(ring, maxPieces, outA, outB);
            int n = ring != null ? ring.Count : 0;
            if (n < 2 || maxPieces <= 0)
                return 0;
            double dash = dashM, period = (double)dashM + gapM;
            double pos = 0.0;   // perimeter distance at the current edge's start
            int added = 0;
            for (int i = 0; i < n; i++)
            {
                float3 a = ring[i], b = ring[(i + 1) % n];
                if (!Finite(a) || !Finite(b))
                    continue;
                double L = LengthXZ(a, b);
                double t = 0.0;
                while (t < L)
                {
                    double p = (pos + t) % period;
                    double next;
                    if (p < dash)
                    {
                        next = Math.Min(L, t + dash - p);
                        if (next - t >= MinPieceM)
                        {
                            outA.Add(Lerp(a, b, t / L));
                            outB.Add(Lerp(a, b, next / L));
                            if (++added >= maxPieces)
                                return maxPieces;
                        }
                    }
                    else
                    {
                        next = Math.Min(L, t + period - p);
                    }
                    if (!(next > t))
                        next = L;   // numerical stall (period below t's resolution): finish the edge
                    t = next;
                }
                pos += L;
            }
            return added;
        }

        private static bool Finite(float3 v)
        {
            return !float.IsNaN(v.x) && !float.IsInfinity(v.x) && !float.IsNaN(v.y) && !float.IsInfinity(v.y) &&
                   !float.IsNaN(v.z) && !float.IsInfinity(v.z);
        }

        private static double LengthXZ(float3 a, float3 b)
        {
            double dx = (double)b.x - a.x, dz = (double)b.z - a.z;
            return Math.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>a + (b - a) * f in double, per component.</summary>
        private static float3 Lerp(float3 a, float3 b, double f)
        {
            return new float3(
                (float)(a.x + ((double)b.x - a.x) * f),
                (float)(a.y + ((double)b.y - a.y) * f),
                (float)(a.z + ((double)b.z - a.z) * f));
        }
    }
}
