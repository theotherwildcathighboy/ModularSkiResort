# SkiRunSurfaceSystem: why props on ski runs need Game.Objects.Surface, and why it runs right after WetnessSystem at interval 256

Makes props standing on our ski runs whiten in step with the snow we inject. Citations are the `Game.dll` 1.6.0f1 decompile baseline.

## Why

Every imported static object's shader has a snow layer fed per instance from `Game.Objects.Surface` via `colossal_Wetness`; an object
without its own `Surface` walks up `Owner` to find one (BatchDataSystem.cs:801-816). Our snow guns are sub-objects of the run AREA,
and `SurfacePrefab` adds `Game.Areas.Surface`, a one-byte empty tag (Game.Areas/Surface.cs:6-9), not the weather struct. Nothing
gives an area entity `Game.Objects.Surface`, so the owner walk finds nothing and props on our runs render snow-free in every season.
The WeatherObject RequireSnow gate (SubObjectSystem.cs:3231) reads the same lookup, so snow-cap sub-objects never fire either.

The fix is to give the run area that component and keep it in step with the depth `SkiSnowInjectionSystem` measures
(`SkiRunPropSystem` already adds runtime components to these same area entities).

## Phase and interval are both load-bearing

* Phase: `BatchDataSystem` refreshes `ObjectProperty.SurfaceWetness` only when `m_WetnessSystem.LastSystemVersion` moves
  (`GetDataQuery` :2572, 2604-2609), and a mod write does not bump it, so we must write in the same tick vanilla does:
  `UpdateAfter<WetnessSystem>`.
* Interval: `UpdateSystem` inherits an anchor's runtime offset only when the intervals are equal (UpdateSystem.cs:443-452).
  `WetnessSystem` is 256, so we are 256; otherwise the anchor would be decorative.

## The tug-of-war with vanilla is safe

`WetnessSystem` applies one small speed-clamped step per pass (at +3 C it sheds ~7/255 of `m_SnowAmount`), and running right after it
our write dominates. GameSimulation runs in LateUpdate and the render consumer in the next frame's Update, so our value is consumed
once and holds for the 256-frame window.

We set the value both ways rather than clamping up: upward-only would make the white wash immortal (~255 passes to clear at +3 C once
only vanilla lowers it). Props go white as the ribbon builds and come clean as it melts.

## Scope and depth source

Game mode only; only our run prefabs are touched. `m_Wetness` and `m_Dirtyness` stay with their vanilla owners. The depth comes from
`SkiSnowInjectionSystem.TryGetDressDepth`: the managed mean (bound injected runs) or, with no managed measurement this load, the
natural runs' measured mean; with neither, vanilla is left alone. The pass line names the source.

## Log flood window

The value moves on every 256-frame pass while snow builds, so pass lines go through a `LogWindow` (at most one per `Tuning.LogWindowS`
of wall clock); the rest fold into one `Surface: N more pass line(s) in T s, folded ...` line (snow min-max /255, then the last
pass's full line). A pass that adds the `Game.Objects.Surface` component is forced. A fold is flushed before a load (`OnGamePreload`)
and at quit (`OnDestroy`). The unchanged-value heartbeat is wall clock (`Tuning.HeartbeatS`).
