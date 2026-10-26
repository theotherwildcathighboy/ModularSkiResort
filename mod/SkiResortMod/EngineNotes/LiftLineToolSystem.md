# LiftLineToolSystem: how the lift-line tool plugs into the net pipeline and emits a fitted line

`LiftLineToolSystem` is our own lift-line tool. The node set of the line is the planner's plan: click the base station, click the top
station, see the planned towers in an overlay preview, drag / add / remove towers (the planner re-plans around the user's pins), then
Build: the line is emitted as one CreationDefinition + NetCourse per planned span and goes through the game's own apply path (validation,
cost, ApplyNetSystem). The vanilla net tool stays the fallback (`LiftTowerFitSystem` fits whatever it builds). The hub's lift-line tiles
open this tool with their lift type (`TryActivate(why, type)`); the line prefabs have UIObject m_Group null, so the hub tile is the only
way in. Enter on the default plan builds what quick-place built, with fewer, fitted towers.

## Seam into the engine
Re-derived from the vanilla net tool; nothing is copied from the ParkManager reference (GPL). Citations are the decompile baseline 1.6.0f1.
- A prefab-less `ToolBaseSystem` (the `SkiGladeToolSystem` skeleton): `base.OnCreate` registers the tool and its Apply / Secondary Apply /
  Cancel proxies (ToolBaseSystem.cs:292-316); `TrySetPrefab` returns false (ToolSystem.cs:263-277); activation is `ToolSystem.activeTool`
  (ToolSystem.cs:84-98) from `LiftLineHotkeySystem`; the raycast is terrain only (`InitializeRaycast`).
- Per span one entity `CreationDefinition{m_Prefab = the line net, SubElevation}` + `NetCourse` + `Updated` through the
  ToolOutputBarrier's ECB, the shape of `NetToolSystem.CreateStraightLine` (NetToolSystem.cs:3603-3660): StraightCurve, IsFirst on the
  first start, IsLast on the last end, IsLeft | IsRight everywhere, m_FixedIndex -1, m_ParentMesh -1, rotation
  `NetUtils.GetNodeRotation(tangent)`.
- The station ends carry `CoursePos.m_Entity` = the station's stub node (the node vanilla snaps to; GenerateEdgesSystem.cs:1242-1248,
  `NodeMapKey` m_Original). The inner nodes are plain positions shared bit-for-bit by the two courses they join (`NodeMapKey` exact
  float3, GenerateEdgesSystem.cs:59-70), y = ground + E(member), NO FreeHeight, so `CourseSplitSystem.SampleCourseHeight` keeps the y
  (CourseSplitSystem.cs:1643-1700).

## Span length raise
`CourseSplitSystem` cuts any course longer than `NetGeometryData.m_EdgeLengthRange.max` into equal pieces (CourseSplitSystem.cs:3487-3493)
and `ValidationHelpers` flags an edge longer than 1.1 x max (ValidationHelpers.cs:1395-1425). While this tool is open the four line
prefabs' `m_EdgeLengthRange.max` / `m_ElevatedLength` (set from `m_MaxPylonDistance`, NetInitializeSystem.cs:2180-2181) are raised to the
planner's MaxSpanM and restored when it closes (the `CursorAimSystem` precedent: a prefab-entity data write, logged). The vanilla net
tool keeps its own split: a permanent raise cut a long line to one engine node (LiftBatch LP-20).

## Split gate (PlacementFlags.ShoreLine)
`CourseSplitSystem.CheckHeightRange` also cuts a course where its height profile changes elevation class (FreeHeight transition nodes,
CourseSplitSystem.cs:2884-2942), which built tool lines with more edges than planned. While the tool is open the line prefabs carry
`PlacementFlags.ShoreLine`, the one prefab-side gate that skips it (`SetSplitGate`: `sampleTerrain && !RequireElevated && !ShoreLine`,
:2884). Without it the engine cuts a tool course wherever its height profile changes elevation class (`GetElevationType`, :2655-2662:
2 / 4 m above the ground at the piece's sides, m_ElevationLimit 4, NetInitializeSystem.cs:874): a span with an end tower of Elevation
>= 4 m follows the chord where it is above the ground (:1381-1385, :1437-1438), and ground steeper than m_MaxSlopeSteepness is filled
(:1436-1450). Other readers of the bit: `CalculateElevation` (:3574-3592, only over water >= 4 m deep) and `ValidationHelpers.CheckSurface`
(:1604, the underwater check of OnGround nets is skipped); `NetToolSystem` (:6221) only when the vanilla net tool is the active tool.
Same window and restore discipline as the span raise.

Crossings with other HV nets still split; the fit matches a line with extra nodes (`LiftToolLines.MatchSplit`), and a mismatch logs the
split points ("engine split points not in the plan").

## Elevation
`CalculateElevation` re-derives each node's Elevation from the terrain beside it (CourseSplitSystem.cs:3549-3647). The fit system then
writes Elevation = (E, E) exactly, recognizing the line as a tool line through `ToolLines` (`LiftToolLines`): heights only, with the
tool's members, never moved, so a pinned node is never snapped.

## Apply
The frame after the emission the Temps exist and are validated (CourseSplit PostTool, GenerateNodes Modification1, GenerateEdges
Modification2, Validation ModificationEnd). Then `applyMode = Apply` for one frame when `GetAllowApply` holds (no Error,
ToolBaseSystem.cs:533-540) and the definitions are destroyed (`DestroyDefinitions`, :506-518). Errors: Clear, back to editing, WARN with
the count. Cost is the vanilla Temp pricing (Game.Net/CostSystem).

## Edit loop
The "generate, edit, build" pattern; `LiftLineEdit` holds the model, the input rules are the pure `LiftLineInput`, tested in LiftBatch.
- Left click = the first end, then the other end. An end is a station's stub (a station that already has its line is refused: bulldoze the
  section to rework, then click its loose ends), a loose end of a lift line of the same type (a node with one lift-line edge and no
  station: what a bulldozed section leaves, so a section is redrawn between its two neighbors), or, once the type is known (hub tile, or
  the first end), open ground (a fresh end node).
- Left click on the line = add a tower (pinned); press on a tower and drag = move it; release it off the line = remove it.
- Right click = step back one action (cancel a drag, undo the last edit, drop the second end, drop the first end) and close the tool only
  when nothing is left. Build key (Enter by default) = build; Esc = the frontend's Back, which closes the tool (unverified in game whether
  Esc also fires the tool's Cancel action: then it steps back).
- Right click is the tool's Cancel action ONLY; Secondary Apply stays off, because both are the right mouse button by default and with
  both live one click removed a tower AND dropped the line.

## Preview
Drawn into `OverlayRenderSystem.Buffer` at `ToolUpdate`, cleared every frame (OverlayRenderSystem.cs:738-790). The planned rope is colored
by the lowest carrier's floor margin (`LiftPlanDraw`: green >= 2 m, amber 0-2, red below the floor, violet above the max rope height).
Every tower gets a ground ring colored by its spans (green clear, red a span below the floor = SHORT, violet a span above the max rope
height = ROPE TOO HIGH), a blue inner ring when pinned, and a cross-bar at rope height sized by the member; the hovered tower is white;
the dragged tower is a white ring at the cursor. Span lengths and the plan line are in the tooltip (`LiftLineTooltipSystem`) and in the log.

## Refusal diagnosis (2026-10-26)
A refused Build (GetAllowApply false: the `Error` query, ToolBaseSystem.cs:313, 533-540) always logs every flagged entity: edge / node /
object, prefab, Temp flags and original, along / lateral on the line, and each icon's error type (IconElement -> icon PrefabRef ->
ToolErrorData.m_Error; ValidationSystem.AddIcon, ValidationSystem.cs:1747-1752) with the Target the icon names, plus a per-type
summary in the warning and the tooltip. Temp edges are counted as created (TempFlags.Create: the definition had no original) vs
existing edges touched at an end (a Temp copy with m_Original; GenerateEdgesSystem.cs:1371-1414): field test 2's "planned 8 / Temp 9"
was the loose end's existing edge. The plan log prints the max edge slope against the prefab's limit and any span TOO STEEP (red in the
preview); PatchSpans logs each line prefab's live m_MaxSlopeSteepness against the planner's. A station whose carriers leave below the
ground (EndCarrier < -0.25 m) logs SUNK STATION on every plan and shows one tooltip notice per line.

## Logs
Silence is impossible: ARMED / patch lines per load; one line per activation, station pick, plan (towers, member mix, spans, clearance,
pins), edit, build (courses emitted), apply (Temps, errors, registered tool line). An exception disables the tool until the next load (one
"!!!" ERROR with the stack).
