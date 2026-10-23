# HubCameraRules: how the camera's pivot and zoom are read for the "already on this sign" test

The test is `zoom <= KeepMaxZoomM` and `d <= KeepZoomFraction x zoom` (both inclusive), with `d` the horizontal (x, z) distance from the
camera pivot to the sign. Engine citations are `File.cs:line` against the Game.dll decompile baseline 1.6.0f1.

- Pivot and zoom come from `IGameCameraController` (`Game.Rendering/IGameCameraController.cs:8-10`). The pivot is the screen center: the
  gameplay camera's `m_Pivot` (`CameraController.cs:176-186`), the orbit camera's anchor (`OrbitCameraController.cs:75-85`), which a
  follow glides onto the followed entity within 0.5 s (:262-266). Zoom is about the camera-to-pivot distance (`CameraController.cs:224-234`,
  :405, :476-479; the orbit camera sits at zoom + radius, `OrbitCameraController.cs:272`), good enough for a keep radius.
- Zoom-relative: zoomed in at 150 m a sign 100 m off-center is at the screen edge; at 1,000 m, 300 m off is still central.
- Screen center, not "on screen": the hub panel covers the right quarter and can be dragged (C# cannot see where), so a viewport test
  would call a sign hidden under the panel "in view". The pivot is never covered.
- Above `KeepMaxZoomM` the sign is a few pixels even at the center, so the move zooms in.
- Unknown camera state flies: a non-finite pivot, sign or zoom, or zoom <= 0, gives false.
- `KeepZoomFraction` and `KeepMaxZoomM` are unmeasured estimates (the field of view is scene data, `CameraController.cs:324`).
