# SkiStationAlignSystem: how the station alignment aid picks subject and partner, draws guides and logs placements

Citations are Game.dll 1.6.0f1. The aid is a read-only guide + readout + an "aligned" state at 0.5 m / 0.5 deg. The math is the pure
`StationAlign`; the readout tooltip is `SkiLiftTooltipSystem`, which reads this system's state.

## Why it is read-only

Snapping is not possible inside the vanilla object tool. Its rotation is a private struct in a private NativeReference
(ObjectToolSystem.cs:54-63, :2275) with no public setter, and its only alignment snap is ExistingGeometry onto a net node (from :1025,
the line-first mechanism). This system never writes to a tool, a station or a definition.

## Subject

Only while Settings > Lifts > LiftBuildAids is on; first match wins:

1. The vanilla object tool is the active tool (ToolSystem.activeTool, ToolSystem.cs:84) with a station prefab (ObjectToolSystem.prefab,
   :2396) in Create mode (actualMode, :2323; Mode enum :35-44), or Move mode (the vanilla relocate): subject = the Temp station entity
   (the snapped preview) and its Transform [likely: Temp station previews carry Transform every frame]. Placing with no preview yet
   (cursor off terrain) = no subject; (2) and (3) are NOT consulted while placing.
2. Else `LiftCensusSystem.LastMovedStation` if it moved within MovedWindowS of wall-clock time (Move It nudges, connected or not; the
   reconnect state exists only with a line attached, LiftReconnectSystem's "foreign edge" rule), published as SubjectIsMoved (only this
   subject's rows reach the mouse under a non-default tool, StationAlign header READOUT PLACE).
3. Else ToolSystem.selected (ToolSystem.cs:101) when it is a station.

## Partner

If the subject (a real station) is a terminal of a census row with both stations, the partner is the opposite station of that row (an
already connected top station nudged with Move It). The same for a Move-mode preview through the station it relocates,
`Temp.m_Original` (otherwise the relocated top station of a complete lift would be measured against nothing, or an unrelated free base,
because the free-station rule below skips stations on a Complete row, its own base included).

Else a station of the OPPOSITE prefab kind, not the subject nor its Temp.m_Original, on no Complete census row, within PartnerRadiusM
(planning constant, unverified) and with along > 0 seen from the candidate; smallest bearing error first, then the nearest. The pick is
kept unless another candidate is better by SwitchMarginDeg for SwitchFrames consecutive frames. Partners are measured from LIVE
Transforms (the census station list may lag a moving station by up to `LiftCensusSystem.CoalesceFrames`).

The partner must be a station of the subject's lift type (`StationDef.LiftTypeIndex` through `LiftCensusSystem.TryGetStationDef`): a row
partner of another type (a mixed row) is not used, and a free candidate of another type is never picked. Every stub tip is the station
row's own (`StationAlign.StubTip(pos, rot, StationDef.StubTipLocalZ)`: -16 on the detachable / gondola stations) for the subject,
partner, candidates and log lines; SkiLiftTooltipSystem's readout reads SubjectTip / PartnerTip from here.

## Draw

Rendering phase, anchored before OverlayRenderSystem, which consumes and clears the overlay lists later in the same phase
(OverlayRenderSystem.cs:781, :790; registration in Mod.cs). The partner's axis DASHED from its stub tip to
max(along + PartnerAxisExtraM, PartnerAxisMinM); the subject's axis SOLID SubjectAxisM; a perpendicular tick from the subject tip to its
foot on the partner's axis. Color: neutral, amber when within NearOffsetM and NearYawDeg (and in front), green when aligned. Lines are
Projected (draped, OverlayRenderSystem.StyleFlags.Projected = 2, :99-104). Buffer use is the main-thread pattern of SkiGladeToolSystem:
GetBuffer(out deps) then deps.Complete() before writing (GetBuffer :603-634; DrawLine :207, DrawDashedLine :218).

## Logs (while the aid is on)

- One line per station Created: `Align: placed SkiTopStationBuilding 119398:67 612 m from base 119459:77 - offset 0.2 m right, yaw 0.1
  deg (aligned)` (capped at PlacementLinesPerFrame per frame + one summary line).
- One line per move episode: the pure `StationMoveEpisodes` (StationAlign.cs) keeps one episode per station and closes it after
  `StationMoveEpisodes.QuietSeconds` of wall clock without a move of that station (a station whose height re-settles after placement
  would otherwise log a line per pause): `Align: moved and settled - moved 18 time(s) over 3.7 s; now ...` (final numbers, measured when
  the episode closes; a station removed meanwhile reads "(gone)").
- Created tags are visible at Rendering: they live until Cleanup (PrepareCleanUpSystem is the last MainLoop system, SystemOrder.cs:50,
  after RenderingSystem :68). Frame counts are UnityEngine.Time.frameCount (advances while paused). The log verdict uses
  `StationAlign.Classify`.

## House rules

"created" line; one `Align: ARMED` line per load with every constant; Game mode only; m_Armed / m_Disabled / m_ErrorLogged per load;
one-shot `!!!` catch that disables the aid until the next load. No per-frame managed allocation: queries are probed with
IsEmptyIgnoreFilter, chunk arrays are Allocator.Temp, strings are built only for log lines.
