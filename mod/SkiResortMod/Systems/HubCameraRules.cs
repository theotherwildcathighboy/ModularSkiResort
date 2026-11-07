// Pure "is the camera already on this sign?" test behind the hub's camera moves (resort switch, lodge launch).
// Already on when zoom <= KeepMaxZoomM and d <= KeepZoomFraction x zoom (both inclusive), with d = horizontal (x, z) distance from the
// camera pivot to the sign. The test is zoom-relative. It uses the screen center rather than "on screen" (the hub panel covers the right
// quarter and can be dragged, so a viewport test would call a sign hidden under it "in view"). Above KeepMaxZoomM the move zooms in.
// Unknown camera state flies. A non-finite pivot, sign or zoom, or zoom <= 0, gives false.
// KeepZoomFraction and KeepMaxZoomM are unmeasured estimates. Pure file: Unity.Mathematics only. Linked into the net8 test host.
// Engine notes: EngineNotes/HubCameraRules.md

using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    /// <summary>Pure camera rules of the hub.</summary>
    public static class HubCameraRules
    {
        /// <summary>Keep radius as a fraction of the zoom (tunable, unmeasured).</summary>
        public const float KeepZoomFraction = 0.3f;

        /// <summary>Above this zoom (m) the camera is never "already on" (tunable, unmeasured).</summary>
        public const float KeepMaxZoomM = 1500f;

        /// <summary>The game's upgrade placement range from the owner (ValidationHelpers.cs:2065-2107, m_MaxPlacementDistance 5000).
        /// Only named in the launch line, never a reason to fly.</summary>
        public const float PlacementRangeM = 5000f;

        /// <summary>The keep radius (m) at <paramref name="zoom"/>.</summary>
        public static float KeepRadiusM(float zoom)
        {
            return KeepZoomFraction * zoom;
        }

        /// <summary>True when the camera already looks at the sign.</summary>
        public static bool AlreadyOn(float2 pivotXZ, float zoom, float2 signXZ)
        {
            return AlreadyOn(pivotXZ, zoom, signXZ, out float _);
        }

        /// <summary>As <see cref="AlreadyOn(float2, float, float2)"/>. <paramref name="distanceM"/> is the horizontal pivot-to-sign
        /// distance whatever the verdict (NaN when a position is not finite).</summary>
        public static bool AlreadyOn(float2 pivotXZ, float zoom, float2 signXZ, out float distanceM)
        {
            distanceM = math.distance(pivotXZ, signXZ);
            if (!math.isfinite(distanceM) || !math.isfinite(zoom) || zoom <= 0f)
                return false; // unknown camera state: the caller flies
            return zoom <= KeepMaxZoomM && distanceM <= KeepRadiusM(zoom);
        }
    }
}
