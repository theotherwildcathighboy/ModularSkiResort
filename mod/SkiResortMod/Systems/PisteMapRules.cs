// PisteMapRules — the piste-map overlay's PURE part of Round E1 (decision D19; docs/research/econ-round/
// spec/ch5-hub-csharp.md §6.3, deviation C3): the outline style of a run by its band, the dashed test (natural
// runs), the zoom width scale, and the cutting of a run's closed node ring into the straight pieces
// SkiPisteMapSystem caches and draws. Written 2026-10-04 (Round E1, stream S5). Tested by tests/ResortBatch
// HubRulesTests section F (literal oracles).
//
// RULES:
//   StyleOf      band −2 Unrateable, −1 Pending, 0..4 Flat..DoubleBlack, anything else Pending (the catalog's
//                RunRow.Band, SkiRunCatalogSystem; the hub's band colors, hub.module.scss .band*)
//   IsDashed     kind == RunKind.Natural (3): the natural run draws nothing on the ground (DV20), so its
//                outline is dashed to say "a surface, not a groomed piste"
//   WidthScale   1 up to RefZoomM of camera zoom, then zoom / RefZoomM, at most MaxWidthScale (a projected
//                line keeps its width in meters, so a far camera needs wider lines to see them)
//   SolidRing    one piece per edge of the closed ring (closing edge included), ends finite, xz length
//                >= MinPieceM
//   DashRing     (C3) dashes cut on the CPU along the whole xz perimeter from node 0, dashM on and gapM off,
//                the phase carried across nodes in double, each "on" piece clipped to its edge with y
//                interpolated linearly; pieces shorter than MinPieceM are dropped; an edge with a
//                non-finite end is skipped and adds no length. OverlayRenderSystem.Buffer.DrawDashedLine
//                (OverlayRenderSystem.cs:218-221) restarts its pattern on every edge, so an outline of
//                short edges would read solid — hence the CPU cut. A bad dash or gap → SolidRing.
//   Both ring cutters APPEND to the caller's lists (never clear) and stop at maxPieces (return value =
//   pieces appended by this call; == maxPieces means the cap was reached).
//
// PURE FILE: `using System;`, `using System.Collections.Generic;` and `using Unity.Mathematics;` only (the
// ResortBatch host links it with UnityMathematics.NoDeps). No allocation beyond the caller's lists.

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    /// <summary>D19 outline style by difficulty (the hub's band colors, hub.module.scss .band*).</summary>
    public enum PisteStyle : byte { Pending = 0, Unrateable = 1, Flat = 2, Green = 3, Blue = 4, Black = 5, DoubleBlack = 6 }

    /// <summary>The overlay's pure rules (ch5 §6.3). Header RULES.</summary>
    public static class PisteMapRules
    {
        public const float RefZoomM = 750f;      // width × 1 up to this camera zoom (m)
        public const float MaxWidthScale = 6f;   // and at most × 6
        public const float MinPieceM = 0.05f;    // shorter pieces are not emitted (vanilla drops < 0.01 m, ch5 F7)
        public const int KindNatural = 3;        // = RunKind.Natural (ch2)

        /// <summary>−2 Unrateable, −1 Pending, 0..4 Flat..DoubleBlack, anything else Pending.</summary>
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

        /// <summary>!(zoom &gt; 0) → 1; else clamp(zoom / RefZoomM, 1, MaxWidthScale) (+∞ → MaxWidthScale).</summary>
        public static float WidthScale(float zoom)
        {
            if (!(zoom > 0f))
                return 1f;
            float s = zoom / RefZoomM;
            if (!(s > 1f))
                return 1f;
            return s < MaxWidthScale ? s : MaxWidthScale;
        }

        /// <summary>Every edge of the closed ring (closing edge included) whose ends are finite and whose
        /// xz length ≥ MinPieceM, appended as one piece. Returns the number appended (≤ maxPieces).</summary>
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

        /// <summary>Walks the closed ring's xz perimeter from node 0, alternating dashM "on" and gapM "off"
        /// with the phase carried across nodes (double arithmetic), and appends each "on" piece clipped to
        /// its edge (y interpolated linearly). Edges with a non-finite end are skipped (they add no length).
        /// Pieces shorter than MinPieceM are dropped. !(dashM &gt; 0), !(gapM ≥ 0), or a non-finite dashM or
        /// gapM → SolidRing. Stops at maxPieces (returns maxPieces; the caller logs the truncation).</summary>
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
                        next = L;   // a numerical stall (a period below t's resolution): finish the edge
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

        /// <summary>a + (b − a) × f in double, per component (y linear along the xz length).</summary>
        private static float3 Lerp(float3 a, float3 b, double f)
        {
            return new float3(
                (float)(a.x + ((double)b.x - a.x) * f),
                (float)(a.y + ((double)b.y - a.y) * f),
                (float)(a.z + ((double)b.z - a.z) * f));
        }
    }
}
