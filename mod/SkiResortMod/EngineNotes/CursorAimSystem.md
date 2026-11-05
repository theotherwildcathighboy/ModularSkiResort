# CursorAimSystem: why the net tool's aim plane needs NetGeometryData.m_DefaultSurfaceHeight rewritten

`CursorAimSystem` puts the net tool's aim plane at the tower head for our lift lines; prefab data alone cannot provide this. Engine
citations are `File.cs:line` against the Game.dll decompile baseline 1.6.0f1.

## How the aim plane is chosen

The aim plane does not read the runtime composition at raycast time: `NetToolSystem` line 5893 reads the drawn prefab's
`NetGeometryData` directly:

```
m_PrefabSystem.TryGetComponentData<NetGeometryData>(m_Prefab, out var component4);
m_ToolRaycastSystem.rayOffset = new float3(0, -component4.m_DefaultSurfaceHeight.max - elevation, 0);
```

That is, the cursor aims at a plane `m_DefaultSurfaceHeight.max` above the terrain hit. `m_DefaultSurfaceHeight` is written once at
prefab init (`NetInitializeSystem` line 139, copying the flags=0 composition's `m_SurfaceHeight`, which for surface-less pieces is
`center(m_HeightRange)`, `NetCompositionHelpers` ~986). Our band (FG {4, 8.5}) would aim at about 6 m; this system rewrites the prefab field to the
lift's `AimHeightM` after init, and the aim plane rides the tower head.

## Other consumers of NetGeometryData.m_DefaultSurfaceHeight

Found by grep across all namespaces:

- `NetToolSystem` 5893 (rayOffset) and 5946/5947/5954 (over/underground camera hints): the intended change.
- `IconCommandSystem` 541: notification icon hover height above net nodes (icons drop closer to the hardware), cosmetic.
- `SelectedInfoUISystem` 725/738: camera focus height for a selected net, cosmetic.
- `NetCompositionSystem` 154: seeds `m_SurfaceHeight` only for piece-less compositions (marker-net fallback; our drawn compositions have
  pieces and take `center(m_HeightRange)` from `NetCompositionHelpers`, unaffected).

Not affected: terrain carving (`TerrainSystem.AddSegment` reads composition `m_HeightRange` for our surface-less class) and collision
(HasSurface / piece-driven).
