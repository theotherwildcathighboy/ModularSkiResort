# SkiRunSurfaceSystem: why props on ski runs need Game.Objects.Surface, and why it runs right after WetnessSystem at interval 256

The system makes props that stand on our ski runs whiten in step with the snow we inject. Citations are the `Game.dll` 1.6.0f1 decompile baseline.

## Why

Every imported static object's shader has a snow layer. The layer is fed per instance from `Game.Objects.Surface` via `colossal_Wetness`.
An object without its own `Surface` walks up `Owner` to find one (BatchDataSystem.cs:801-816). Our snow guns are sub-objects of the run AREA.
`SurfacePrefab` adds `Game.Areas.Surface` to the area. That component is a one-byte empty tag (Game.Areas/Surface.cs:6-9), not the weather struct.
Nothing gives an area entity `Game.Objects.Surface`, so the owner walk finds nothing. Props on our runs then render snow-free in every season.
The WeatherObject RequireSnow gate (SubObjectSystem.cs:3231) reads the same lookup, so snow-cap sub-objects never fire either.

The fix is to give the run area that component and keep it in step with the depth that `SkiSnowInjectionSystem` measures
(`SkiRunPropSystem` already adds runtime components to these same area entities).

Placed props are free-standing since the run prop placer, so they have no owner to walk up to. Each prop carries its own `Surface`.
This system writes the run's dress value onto every `SkiRunPropMember`-tagged prop too, cat track props included.

## Phase and interval are both load-bearing

* Phase: `BatchDataSystem` refreshes `ObjectProperty.SurfaceWetness` only when `m_WetnessSystem.LastSystemVersion` moves
  (`GetDataQuery` :2572, 2604-2609). A mod write does not bump it. So we must write in the same tick as vanilla: `UpdateAfter<WetnessSystem>`.
* Interval: `UpdateSystem` inherits an anchor's runtime offset only when the intervals are equal (UpdateSystem.cs:443-452).
  `WetnessSystem` is 256, so we are 256. Otherwise the anchor would be decorative.

## The tug-of-war with vanilla is safe

`WetnessSystem` applies one small speed-clamped step per pass (at +3 C it sheds ~7/255 of `m_SnowAmount`). Our write runs right after it and dominates.
GameSimulation runs in LateUpdate and the render consumer runs in the next frame's Update. So the render consumer reads our value once, and the value holds for the 256-frame window.

We set the value in both directions instead of clamping up. An upward-only write would make the white wash immortal (~255 passes to clear at +3 C once
only vanilla lowers it). Props go white as the ribbon builds and come clean as it melts.

## Scope and depth source

Game mode only. The system touches only our run prefabs. `m_Wetness` and `m_Dirtyness` stay with their vanilla owners. The depth comes from
`SkiSnowInjectionSystem.TryGetDressDepth`: the managed mean (bound injected runs) or, with no managed measurement this load, the
natural runs' measured mean. With neither, the system leaves vanilla alone. The pass line names the source.

## Log flood window

The value moves on every 256-frame pass while snow builds. So pass lines go through a `LogWindow` (at most one per `Tuning.LogWindowS`
of wall clock). The rest fold into one `Surface: N more pass line(s) in T s, folded ...` line (snow min-max /255, then the last
pass's full line). A pass that adds the `Game.Objects.Surface` component is forced. The system flushes a fold before a load (`OnGamePreload`)
and at quit (`OnDestroy`). The unchanged-value heartbeat is wall clock (`Tuning.HeartbeatS`).
