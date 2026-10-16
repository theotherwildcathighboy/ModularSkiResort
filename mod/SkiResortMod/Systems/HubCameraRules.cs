// HubCameraRules — the PURE "is the camera already on this sign?" test behind the hub's two camera
// moves of Round F6 (docs/research/field-test-6-fixes.md items A1, A2; research
// docs/research/field/2026-10-03b/findings/ui-small.md §1.3 and its Verification). Created 2026-10-03
// (Round F6, implementer A). Consumers: SkiResortHubUISystem (‹ › resort switch, lodge launch),
// tests/ResortBatch (literal-oracle cases).
//
// THE RULE. The camera is "already on" a sign when BOTH hold:
//     d    = horizontal distance (x, z) from the active camera's pivot to the sign
//     zoom = the active camera's zoom
//     already on  ⇔  zoom ≤ KeepMaxZoomM  ∧  d ≤ KeepZoomFraction × zoom      (both inclusive)
//   * pivot and zoom: IGameCameraController.pivot / .zoom (Game.Rendering/IGameCameraController.cs:8-10).
//     The pivot is the point the camera looks at, the centre of the screen: the gameplay camera's m_Pivot
//     (Game/CameraController.cs:176-186), the orbit camera's anchor (Game/OrbitCameraController.cs:75-85),
//     which a follow glides onto the followed entity within 0.5 s (:262-266: the remaining offset is scaled
//     by smoothstep(0.5, 0, timer)). zoom is ≈ the camera-to-pivot distance (CameraController.cs:224-234,
//     GetCameraPos :476-479 with the offset dir × zoom :405; the orbit camera sits at zoom + radius,
//     OrbitCameraController.cs:272) — an approximation, good enough for a keep radius (ui-small
//     Verification, correction 4). All four game files are identical in 1.6.2f1.
//   * Zoom-relative, not a fixed radius: zoomed in at 150 m, a sign 100 m off-centre is at the screen's
//     edge; zoomed out at 1,000 m, 300 m off-centre is still near the middle.
//   * Screen centre, not "anywhere on screen": the hub panel covers the right quarter of the screen and
//     can be dragged (C# does not know where), so a viewport test would call a sign hidden under the
//     panel "in view". The pivot is at the centre, which the panel never covers.
//   * The zoom cap: zoomed out over the whole map the sign is a few pixels even at the centre, so above
//     KeepMaxZoomM the move zooms in.
//   * Unknown camera state flies: a non-finite pivot, sign or zoom, or zoom ≤ 0 → false.
//   KeepZoomFraction and KeepMaxZoomM are PLANNING ESTIMATES (tunable, unmeasured): the game's field of
//   view is scene data (CameraController.cs:324 reads it from the lens). The hub's "Hub [focus]" lines
//   print the distance and the zoom of every decision, so both can be tuned from one field report.
//
// PURE FILE: `using Unity.Mathematics;` only (the test-host rule of tests/SlopeBatch; tests/ResortBatch
// links this file into a net8 host). No allocation, no state.

using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    /// <summary>Pure camera rules of the hub (field-test-6-fixes.md A1 / A2).</summary>
    public static class HubCameraRules
    {
        /// <summary>Keep radius as a fraction of the zoom *(tunable, unmeasured)*.</summary>
        public const float KeepZoomFraction = 0.3f;

        /// <summary>Above this zoom (m) the camera is never "already on" *(tunable, unmeasured)*.</summary>
        public const float KeepMaxZoomM = 1500f;

        /// <summary>The game's upgrade placement range from the owner (ValidationHelpers.cs:2065-2107,
        /// m_MaxPlacementDistance 5000). T12: only named in the launch line, never a reason to fly.</summary>
        public const float PlacementRangeM = 5000f;

        /// <summary>The keep radius (m) at <paramref name="zoom"/>: KeepZoomFraction × zoom.</summary>
        public static float KeepRadiusM(float zoom)
        {
            return KeepZoomFraction * zoom;
        }

        /// <summary>True when the camera already looks at the sign (header THE RULE).</summary>
        public static bool AlreadyOn(float2 pivotXZ, float zoom, float2 signXZ)
        {
            return AlreadyOn(pivotXZ, zoom, signXZ, out float _);
        }

        /// <summary>As <see cref="AlreadyOn(float2, float, float2)"/>; <paramref name="distanceM"/> is the
        /// horizontal pivot-to-sign distance whatever the verdict (NaN when a position is not finite).</summary>
        public static bool AlreadyOn(float2 pivotXZ, float zoom, float2 signXZ, out float distanceM)
        {
            distanceM = math.distance(pivotXZ, signXZ);
            if (!math.isfinite(distanceM) || !math.isfinite(zoom) || zoom <= 0f)
                return false;   // unknown camera state: the caller flies
            return zoom <= KeepMaxZoomM && distanceM <= KeepRadiusM(zoom);
        }
    }
}
