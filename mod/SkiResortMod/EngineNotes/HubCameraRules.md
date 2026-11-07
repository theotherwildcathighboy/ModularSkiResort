# HubCameraRules: how the camera's pivot and zoom are read for the "already on this sign" test

The test is `zoom <= KeepMaxZoomM` and `d <= KeepZoomFraction x zoom` (both inclusive). `d` is the horizontal (x, z) distance from the
camera pivot to the sign. Engine citations are `File.cs:line` against the Game.dll decompile baseline 1.6.0f1.

- Pivot and zoom come from `IGameCameraController` (`Game.Rendering/IGameCameraController.cs:8-10`). The pivot is the screen center.
  For the gameplay camera, the pivot is `m_Pivot` (`CameraController.cs:176-186`). For the orbit camera, the pivot is the anchor
  (`OrbitCameraController.cs:75-85`). A follow glides the anchor onto the followed entity within 0.5 s (:262-266).
- Zoom is about the camera-to-pivot distance (`CameraController.cs:224-234`, :405, :476-479). The orbit camera sits at zoom + radius
  (`OrbitCameraController.cs:272`). This is good enough for a keep radius.
- Zoom-relative: at a zoom of 150 m, a sign 100 m off-center is at the screen edge. At 1,000 m, a sign 300 m off is still central.
- Screen center, not "on screen": the hub panel covers the right quarter and the player can drag it (C# cannot see where). A viewport
  test would call a sign hidden under the panel "in view". The pivot is never covered.
- Above `KeepMaxZoomM` the sign is a few pixels even at the center, so the move zooms in.
- Unknown camera state makes the camera fly. A non-finite pivot, sign or zoom, or zoom <= 0, gives false.
- `KeepZoomFraction` and `KeepMaxZoomM` are unmeasured estimates. The field of view is scene data (`CameraController.cs:324`).
