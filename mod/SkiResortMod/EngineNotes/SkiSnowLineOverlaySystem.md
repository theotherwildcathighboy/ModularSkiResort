# SkiSnowLineOverlaySystem: where the winter snow line and the snow-gun line are computed and drawn on the terrain

Citations are Game.dll 1.6.0f1. A summer builder has no snow to read the line from. So the overlay draws two lines. One line (a pale
contour) shows where the climate's mean winter temperature, lapsed with elevation, crosses 0 °C. A fainter, lower line shows where our
snow guns start working (+SnowmakingMargin °C). The tooltip (`SkiSnowLineTooltipSystem`, same file) names both elevations once.

## The numbers

The system computes them once per load (OnGameLoadingComplete). It recomputes them lazily if the climate was not ready. The guns line
follows the Snowmaking margin option.

- winter mean T = mean of hourly `ClimateSystem.SampleClimate(ClimatePrefab, t).temperature` over the climate's winter season. The
  prefab overload ignores the temperature override and the current date (ClimateSystem.cs:607-623), so the answer is the same in August
  and in January. The curve is `ClimatePrefab.m_Temperature` (:628), built from `ClimatePrefab.m_Seasons[].m_TempNightDay` with its
  Gaussian day-to-day noise (ClimatePrefab.RebuildTemperatureCurves :980-998). The window is the SeasonInfo whose name is "SeasonWinter"
  (ClimateSystem.SeasonInfo.name :73-80) from its m_StartTime to the next season's start (vanilla 0.9167 -> 1.1667 =
  December-February). If there is no such season, the window is December-February by the calendar.
- snow line = temperatureBaseHeight + (T_winter - freezingTemperature) / snowTemperatureHeightScale
- guns line = temperatureBaseHeight + (T_winter - freezingTemperature - margin) / snowTemperatureHeightScale

These formulas solve the game's own lapse T_local(h) = T - (h - base) x 0.01 (`SnowSystem.SnowTransfer`) for the thresholds.
`SnowLineSystem.GetSnowLineElevation` and the injection's gates use these thresholds (`RunSnowRules.ThresholdC`). "Sea level" is the
lapse's reference elevation temperatureBaseHeight (ClimateSystem.cs:226).

## When it shows

The triggers are the same family as SkiRunContourSystem and the run tooltip, plus the sign. All of these must hold: Game mode, option
ShowSnowLineWhilePlacing on, ownership prefab sets resolved, and the active tool is one of these:

- the area tool holding one of our run prefabs (drawing or editing). The hub's run buttons start the same tool.
- the object tool placing the Ski Area Welcome Sign. ToolBaseSystem.GetPrefab is null in Upgrade/Move mode (ObjectToolSystem.cs:2836-2847),
  so a sign upgrade does not show it.

SkiPlacementInfoviewSystem is not the trigger: it only reacts to our lift LINES (it closes their Electricity view).

## Near the draw

The overlay uses a world-anchored 96 x 96 grid at 40 m (3.8 km square, 9,216 height samples, <= 10 k). The grid is around the camera pivot (IGameCameraController.pivot, the CameraPoint rule of the hub).

- The system samples heights with `TerrainUtils.SampleHeight` on the main thread (GetHeightData, no reader registration: the glade tool's
  precedent, SkiGladeToolSystem.cs:2306).
- `SnowLineRules.MarchingSquares` cuts the samples into straight segments for the two levels (<= 1,536 each).
- The system rebuilds only when the tool becomes active, the pivot moved more than 100 m, or an elevation changed. Every other frame it
  only re-adds the cached segments.
- Segments go into `OverlayRenderSystem.Buffer.DrawLine` with StyleFlags.Projected (drapes on the terrain, OverlayRenderSystem.cs:99-104,
  :207-211). The writes happen on the main thread, as the glade tool does (:3560-3567). They complete the buffer's earlier writers first
  (GetBuffer + Complete, :603-634) and need no AddBufferWriter.
- Width = base x `PisteMapRules.WidthScale(zoom)`, so the line stays readable when the camera is high.

## Phase and cost

The system runs in the Rendering phase, anchored `UpdateBefore<SkiSnowLineOverlaySystem, Game.Rendering.OverlayRenderSystem>` (Mod.cs).
The anchor is load-bearing: OverlayRenderSystem uploads and clears its lists in the same frame (:738-741, :781, :790).

Cost:

- Idle (tool not ours, option off, not armed) = a settings bool and two reference compares.
- Active and unchanged = two cached counts and <= 3,072 DrawLine calls.
- A rebuild = 9,216 bilinear samples + two marching passes. The system warns once per load about a build over SlowBuildWarnMs. StateText
  shows both times.

Timings unmeasured.

## Logs

The system writes these log lines:

- `SkiSnowLineOverlaySystem created`
- ONE INFO line per load (ARMED) with the elevations and the climate inputs ("SnowLine: ARMED - winter mean ... snow line ... guns line ...")
- one WARN when the climate is missing, once per load
- one WARN for a slow build

A one-shot `!!!` switches the system off until the next load. Nothing is logged per frame. Game mode only.

## The cursor lines and the site survey (2026-11-06)

- **Here:** the terrain height (`TerrainUtils.SampleHeight`) under the tool's raycast hit (`ToolRaycastSystem.GetRaycastResult`,
  ToolRaycastSystem.cs:57-67 in 1.6.2f1). `RaycastSystem.GetResult` completes the raycast the tool already ran this frame, so the read
  at Rendering costs nothing extra. The line shows while the overlay shows (runs and the sign). It shows the height and the distance
  above or below the snow line. There are two keys, HERE_ABOVE / HERE_BELOW, so no signed number.
- **Survey** (only while the object tool places the Welcome Sign):
  - `SkiAreaRadiusRules.Survey` covers the disc of the ski area radius around the cursor. It uses 72 x 72 cell centers over the bounding
    box (cell = 2R / 72, ~4,070 samples in a full disc at every radius).
  - The survey clips to `TerrainSystem.playableOffset` .. `+ playableArea` (TerrainSystem.cs:2514-2516). The world map outside is not
    buildable.
  - It leaves out water deeper than 0.5 m (`WaterUtils.SampleDepth`, the cat track grade's pattern: GetSurfaceData, deps.Complete()).
  - The system redoes the survey when the cursor moved more than 50 m, or the radius or a line changed. It redoes it at most once per 6
    frames.
  - It writes one `SnowLine [cost] survey` INFO line per load (the first survey), and a WARN once over 3 ms.

  The line also names the skiable vertical, MaxM - max(MinM, snow line) (AREA_SURVEY), or says no ground reaches the snow line
  (AREA_SURVEY_NO_SNOW). Water at the cursor's own spot still shows "Here". The sign cannot be placed there anyway.
