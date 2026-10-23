// Pure "is the camera already on this sign?" test behind the hub's camera moves (resort switch, lodge launch).
//
// Already on  <=>  zoom <= KeepMaxZoomM  and  d <= KeepZoomFraction x zoom   (both inclusive), with d the horizontal (x, z)
// distance from the camera pivot to the sign.
//   * pivot and zoom come from IGameCameraController (Game.Rendering/IGameCameraController.cs:8-10). The pivot is the screen
//     center: the gameplay camera's m_Pivot (CameraController.cs:176-186), the orbit camera's anchor
//     (OrbitCameraController.cs:75-85), which a follow glides onto the followed entity within 0.5 s (:262-266). zoom is about
//     the camera-to-pivot distance (CameraController.cs:224-234, :405, :476-479; the orbit camera sits at zoom + radius,
//     OrbitCameraController.cs:272), good enough for a keep radius.
//   * Zoom-relative: zoomed in at 150 m a sign 100 m off-center is at the screen edge; at 1,000 m, 300 m off is still central.
//   * Screen center, not "on screen": the hub panel covers the right quarter and can be dragged (C# cannot see where), so a
//     viewport test would call a sign hidden under the panel "in view". The pivot is never covered.
//   * Above KeepMaxZoomM the sign is a few pixels even at the center, so the move zooms in.
//   * Unknown camera state flies: a non-finite pivot, sign or zoom, or zoom <= 0, gives false.
// KeepZoomFraction and KeepMaxZoomM are unmeasured estimates (the field of view is scene data, CameraController.cs:324).
//
// Pure file: Unity.Mathematics only; linked into the net8 test host. No allocation, no state.

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

        /// <summary>As <see cref="AlreadyOn(float2, float, float2)"/>; <paramref name="distanceM"/> is the horizontal pivot-to-sign
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
