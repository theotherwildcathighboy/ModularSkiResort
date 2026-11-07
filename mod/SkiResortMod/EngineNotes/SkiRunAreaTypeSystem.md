# SkiRunAreaTypeSystem: why the cat track prefab is switched to AreaType Space

The system sets the `AreaGeometryData.m_Type` of the cat track prefab to Space at every Game load, so a drawn cat track may cross other runs
(decompile 1.6.0f1).

## Why Space

All our runs are Surface areas. Drawing one across another fails validation. ValidationHelpers.cs:551 compares only the `m_Type` of the two
prefabs. The exemption at :564-570 needs `m_TopLevelEntity != m_AreaEntity` (false for a drawn run). So the game intersects the triangles
(:575). An overlap becomes `ErrorType.OverlapExisting` at severity Error (:582), which refuses the click
(ValidationSystem.cs:1657-1660, ToolBaseSystem.cs:533-540).

A different type is exempt unless the existing area has PhysicalGeometry or ProtectedArea (:553-566). Surface and Space prefabs get
flags 0 (AreaInitializeSystem.cs:343), and `AreaUtils.SetCollisionFlags` adds Physical only in the editor (AreaUtils.cs:679-689). So in
Game mode a cat track neither collides with nor is collided with by other runs.

## Why not the alternatives

- Not a "SpacePrefab": GeometrySystem gives Space a flat height range (GeometrySystem.cs:134, :372-403), and every `Surface` query of
  ours would need `Space` too.
- Not `AreaType.None`: the area batch indexes `m_AreaTypeData[(int)type]` (AreaBufferSystem.cs:795, table [5] at :652-657), so -1 would
  throw.

## Why it is re-applied at every load

Prefab entities live for the app run, and AreaInitializeSystem fills AreaGeometryData on the Created tag only. A save carries no prefab
data, so the system writes the value again at every load (like LodgeRoadSystem and CursorAimSystem).

## Player-visible effects (unverified until played)

- The area tool snaps only to areas of the same type (AreaToolSystem.cs:171, :322), so a cat-track corner snaps to other cat tracks only.
- The Space snap set adds `Snap.ObjectSurface` (:3282-3286) and a height weight of 1 (:372, :514, :537, :603, :669).
- The raycast type mask becomes Space (:3053), so other runs are not hover targets while drawing.

## Editor

A map-editor load after a game in the same app run would keep Space on the prefab. So the system writes the game's value back, only when the
prefab still holds exactly what we wrote (like `LodgeRoadSystem.RestoreForEditor`).

## Logs

"created". Per Game load ONE line `RunAreaType: prefab SkiRunCatTrackA AreaGeometryData.m_Type Surface (4) -> Space (3) ...`, or the
"already Space" line, or a WARN when the game set something else. A missing prefab logs ERROR and writes nothing. The system only calls
SetComponentData of vanilla AreaGeometryData on the one prefab entity: no Harmony, no reflection.
