# SkiSnowLineOverlaySystem: where the winter snow line and the snow-gun line are computed and drawn on the terrain

Citations are Game.dll 1.6.0f1. A summer builder has no snow to read the line from, so the overlay draws where the climate's mean winter
temperature, lapsed with elevation, crosses 0 °C (a pale contour) and, fainter and lower, where our snow guns start working
(+SnowmakingMargin °C). The tooltip (`SkiSnowLineTooltipSystem`, same file) names both elevations once.

## The numbers

Computed once per load (OnGameLoadingComplete); recomputed lazily if the climate was not ready, and the guns line follows the Snowmaking
margin option.

- winter mean T = mean of hourly `ClimateSystem.SampleClimate(ClimatePrefab, t).temperature` over the climate's winter season. The
  prefab overload ignores the temperature override and the current date (ClimateSystem.cs:607-623), so the answer is the same in August
  and in January. The curve is `ClimatePrefab.m_Temperature` (:628), built from `ClimatePrefab.m_Seasons[].m_TempNightDay` with its
  Gaussian day-to-day noise (ClimatePrefab.RebuildTemperatureCurves :980-998). The window is the SeasonInfo whose name is "SeasonWinter"
  (ClimateSystem.SeasonInfo.name :73-80) from its m_StartTime to the next season's start (vanilla 0.9167 -> 1.1667 =
  December-February); no such season -> December-February by the calendar.
- snow line = temperatureBaseHeight + (T_winter - freezingTemperature) / snowTemperatureHeightScale
- guns line = temperatureBaseHeight + (T_winter - freezingTemperature - margin) / snowTemperatureHeightScale

That is the game's own lapse T_local(h) = T - (h - base) x 0.01 (`SnowSystem.SnowTransfer`) solved for the thresholds
`SnowLineSystem.GetSnowLineElevation` and the injection's gates use (`RunSnowRules.ThresholdC`). "Sea level" is the lapse's reference
elevation temperatureBaseHeight (ClimateSystem.cs:226).

## When it shows

The same family of triggers as SkiRunContourSystem and the run tooltip, plus the sign: Game mode, option ShowSnowLineWhilePlacing on,
ownership prefab sets resolved, and the active tool is

- the area tool holding one of our run prefabs (drawing, editing; the hub's run buttons start the same tool), or
- the object tool placing the Ski Area Welcome Sign (ToolBaseSystem.GetPrefab is null in Upgrade/Move mode, ObjectToolSystem.cs:2836-2847,
  so a sign upgrade does not show it).

SkiPlacementInfoviewSystem is not the trigger: it only reacts to our lift LINES (it closes their Electricity view).

## Near the draw

A world-anchored 96 x 96 grid at 40 m (3.8 km square, 9,216 height samples, <= 10 k) around the camera pivot (IGameCameraController.pivot,
the CameraPoint rule of the hub), sampled with `TerrainUtils.SampleHeight` on the main thread (GetHeightData, no reader registration: the
glade tool's precedent, SkiGladeToolSystem.cs:2306), cut into straight segments by `SnowLineRules.MarchingSquares` for the two levels
(<= 1,536 each). Rebuilt only when the tool becomes active, the pivot moved more than 100 m, or an elevation changed; every other frame
it only re-adds the cached segments. Segments go into `OverlayRenderSystem.Buffer.DrawLine` with StyleFlags.Projected (drapes on the
terrain, OverlayRenderSystem.cs:99-104, :207-211) on the main thread, as the glade tool does (:3560-3567): the writes complete the
buffer's earlier writers first (GetBuffer + Complete, :603-634) and need no AddBufferWriter. Width = base x
`PisteMapRules.WidthScale(zoom)`, so the line stays readable when the camera is high.

## Phase and cost

Rendering phase, anchored `UpdateBefore<SkiSnowLineOverlaySystem, Game.Rendering.OverlayRenderSystem>` (Mod.cs; load-bearing:
OverlayRenderSystem uploads and clears its lists in the same frame, :738-741, :781, :790).

Cost: idle (tool not ours, option off, not armed) = a settings bool and two reference compares; active and unchanged = two cached counts
and <= 3,072 DrawLine calls; a rebuild = 9,216 bilinear samples + two marching passes (a build over SlowBuildWarnMs is warned once per
load; both times are in StateText). Timings unmeasured.

## Logs

`SkiSnowLineOverlaySystem created`; ONE INFO line per load (ARMED) with the elevations and the climate inputs ("SnowLine: ARMED - winter
mean ... snow line ... guns line ..."); one WARN when the climate is missing, once per load, one WARN for a slow build; a one-shot `!!!`
switches it off until the next load. Nothing per frame. Game mode only.

## The cursor lines and the site survey (2026-11-06)

- **Here:** the terrain height (`TerrainUtils.SampleHeight`) under the tool's raycast hit (`ToolRaycastSystem.GetRaycastResult`,
  ToolRaycastSystem.cs:57-67 in 1.6.2f1; `RaycastSystem.GetResult` completes the raycast the tool already ran this frame, so the read
  at Rendering costs nothing extra). Shown while the overlay shows (runs and the sign), as height and the distance above or below the
  snow line (two keys, HERE_ABOVE / HERE_BELOW, so no signed number).
- **Survey** (only while the object tool places the Welcome Sign): `SkiAreaRadiusRules.Survey` over the disc of the ski area radius
  around the cursor, 72 x 72 cell centers over the bounding box (cell = 2R / 72, ~4,070 samples in a full disc at every radius),
  clipped to `TerrainSystem.playableOffset` .. `+ playableArea` (TerrainSystem.cs:2514-2516; the world map outside is not buildable),
  water deeper than 0.5 m left out (`WaterUtils.SampleDepth`, the cat track grade's pattern: GetSurfaceData, deps.Complete()). Redone
  when the cursor moved more than 50 m, the radius or a line changed, at most once per 6 frames. One `SnowLine [cost] survey` INFO line
  per load (the first survey), a WARN once over 3 ms. The line also names the skiable vertical, MaxM - max(MinM, snow line) (AREA_SURVEY), or says no ground reaches the snow line (AREA_SURVEY_NO_SNOW). Water at the cursor's own spot still shows "Here": the sign cannot be placed
  there anyway.
