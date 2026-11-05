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
the dragged tower is a white ring at the cursor. The plan line and the hovered tower's height are in the tooltip (`LiftLineTooltipSystem`);
span lengths and member mix stay in the log.

## Ghost preview (2026-10-26)
In Edit the tool keeps the plan's courses standing as the game's own Temp preview: the translucent cable and tower models at their
planned heights, with the game's error icons. Vanilla pattern (NetToolSystem.cs:6867-6916, ObjectToolSystem.cs:4041-4118): applyMode
None on unchanged frames (Temps stay; ToolOutputSystem.cs:22-30), Clear + DestroyDefinitions + re-emit in one frame when the plan's
`Revision` changes (old Temps are recycled, GenerateNodesSystem.cs:1397-1404, GenerateEdgesSystem.cs:675-680). Definitions without
`Updated` generate nothing more (GenerateNodesSystem.cs:2030-2041; CleanUpSystem strips Updated, CleanUpSystem.cs:24-32). Temps render
on MeshLayer.Outline (BatchInstanceSystem.cs:446-459, 589-609); tower sub-objects copy the owner's Temp flags (SubObjectSystem.cs:
2832-2841). ValidationSystem checks only Temp + Updated (ValidationSystem.cs:1917, 1926-1930), so errors cost one pass per re-emit.
Hidden while a tower is dragged; cleared on leaving Edit (the pick branch). Build clears the ghost and emits afresh (state Emitted,
the old apply / refuse path); a refusal returns to Edit and the ghost comes back with its icons. Census, chair pins
and the fit already handle Temp lift nets (the vanilla net tool makes the same). Park Manager has no Temp ghost (overlay only; its
build is our emitted-then-apply path). Logs: `LiftLine [ghost]` (the first three previews, then every 25th).

### Ghost cleanup on stop (2026-10-26)
Every way out (Esc, another tool, the hub, a load) changes `ToolSystem.activeTool`; the next `ToolUpdate` disables our tool and updates
it once, so `OnStopRunning` runs (ToolSystem.cs:312-318; a load gets there through PreDeserialize, activeTool = default,
ToolSystem.cs:610-614). Vanilla does NOT clear on stop: ToolBaseSystem.OnStopRunning has no Temp code (:333-339), NetToolSystem has
no OnStopRunning, and its Temps go only when the NEXT tool's first update sets applyMode Clear (m_ForceUpdate, ToolBaseSystem.cs:
325-330; DefaultToolSystem.cs:973-980), because ToolSystem.applyMode is the last tool's (ToolSystem.cs:147-155) and ToolOutputSystem
runs the ClearTool phase from it (ToolOutputSystem.cs:22-30). We do not rely on that: `OnStopRunning` destroys the standing
definitions and, when a ghost or a Build emission was standing, runs the game's own `ToolClearSystem` itself (`ClearOwnTemps`): the
same ClearEntitiesJob as vanilla's Clear (Deleted on every Temp, Hidden originals shown again, ToolClearSystem.cs:63-113), its
commands on the ToolOutputBarrier, which plays back later in the same frame (UpdateAfter ToolUpdate, SystemOrder.cs:695). The stop
update runs before the new tool's update, so every Temp then is ours; a second clear by the next tool is a no-op. With no Temp the
system does not run (RequireForUpdate, ToolClearSystem.cs:219). `OnGameLoadingComplete` also resets the ghost fields. Log:
`LiftLine [ghost]: preview cleared on stopped (ToolClearSystem run by the tool itself)`. Not testable in LiftBatch (no ECS world):
owner test step in docs/research/lifts/lift-pass-2026-10-26.md section 10.

## Player text (2026-10-26)
Every tooltip and notice is a LocaleEN `SkiResort.LiftTool.*` key read through `LocalizedString.Id(key, args)` (numbers as
LocalizedNumber<int> "integer"): `LiftLineInput.PromptKey` / `PlanKey` / `TowerKey`, `LiftLineToolSystem.T` / `RefusedNotice` /
`RefusedBuild` (REFUSED[the error type the game flagged most]). The log keeps the detail (`Describe`, `DescribeErrors`). LiftBatch
LP-31 gates them: every shown key exists, at most 25 words, no em dash, no log syntax, only the ARGs the code passes, no stale key.

## Refusal diagnosis (2026-10-26)
A refused Build (GetAllowApply false: the `Error` query, ToolBaseSystem.cs:313, 533-540) always logs every flagged entity: edge / node /
object, prefab, Temp flags and original, along / lateral on the line, and each icon's error type (IconElement -> icon PrefabRef ->
ToolErrorData.m_Error; ValidationSystem.AddIcon, ValidationSystem.cs:1747-1752) with the Target the icon names, plus a per-type
summary in the warning and the tooltip. Temp edges are counted as created (TempFlags.Create: the definition had no original) vs
existing edges touched at an end (a Temp copy with m_Original; GenerateEdgesSystem.cs:1371-1414): field test 2's "planned 8 / Temp 9"
was the loose end's existing edge. The plan log prints the max edge slope against the prefab's limit and any span TOO STEEP (red in the
preview: the tower rings and, via MarkSteepSpans, the span's segments, so a tower-free station-to-station span shows it too). The tool
plans with the line prefab's live m_MaxSlopeSteepness (NetGeometryData, `With(maxEdgeSlope:)` at the pick); PatchSpans logs each line
prefab's live value against the planner's default. A station end whose carriers leave below the
ground (EndCarrier < -0.25 m, `LiftLineInput.SunkKey`) logs SUNK STATION on every plan and shows one standing tooltip notice: SUNK for
a picked station, SUNK_END for a ground end (the station that will finish it would stand on the lower ground ahead: past a crest,
FIX-LIST 55); never a loose end. A tower whose rope bends past its sheave train's cap (`LiftTowerPlan.IsOverCap`) is drawn red, the plan
line reads PLAN_TRAIN and hovering the tower reads TOWER_TRAIN (bend and cap); the plan line is green only for a sound plan (FIX-LIST 55). End ropes (`EndRope`): a picked station reads its lowest HV rope anchor, a loose end the
node formula, a ground end the anchor of the station that will finish it over the terrain at that station's lot front
(`LiftLineInput.GroundEndRope`, LiftLineEdit.md "Ground ends", FIX-LIST 48).

## Carriers passable while the tool runs (2026-11-03, DECISIONS 582)
A line drawn to an existing tower (the redo of a bulldozed middle section, an extension from a loose end) gives the touched edges,
their end nodes and those nodes' towers Temp copies. ValidationHelpers walks each permanent chair to its top-level owner, an edge that
is not overridable, so a chair inside a Temp tower's bounds draws OverlapExisting (field/2026-11-02/lifticon: a Temp FG12 against two
parked chairs about 2 m from it). Moving chairs ride through every tower and a broken line parks them where they stand, so the refusal
depends on where chairs stopped. `SetCarriersPassable` puts GeometryFlags.Marker on the carrier prefabs (an empty collision mask,
ObjectUtils.GetCollisionMask) for the whole tool session (OnStartRunning) and hands back to the station window (`m_StationPlacing`) in
OnStopRunning; SetStationPlacing touches the carriers only while the tool is not running. While set, carriers also skip the raycast
and camera collision; rendering is unchanged. Since the carrier grip round a carrier is two objects: the body (a carrier,
`CarrierGripTops`) and its grip (`GripTypes`: SkiLiftChair4Grip, SkiTramCarriage, ...), which is not a carrier. The grips get the flag
too: without it the bridge check of the section delete (2026-11-03, F1) was refused next to riding chairs, OverlapExisting on the grips
against the neighbor sections' Temp copies (12 SkiLiftChair4Grip on one FG section; a SkiTramCarriage on the tram's 46 m span). Gate:
check-lift-align CARRIERPASS (4 rules, the last one the grip list).

**The window stays the whole session (2026-11-05, DECISIONS 601 e):** the redo needs it whenever Temp copies of existing towers are
validated, and that is every ghost re-emit in Edit (the ghost stands for the whole Edit state) plus the Build; only the two pick steps
have no Temps, and toggling prefab flags at each pick / edit change would buy nothing. Its side effect, trees not hidden by chairs
during a tool session but hidden outside it, was half of the pop-in the owner saw (note 8); the tree corridor
(EngineNotes/LiftTreeCorridor.md) removes the trees in the carrier sweep, so the Marker state no longer changes what a player sees.

## Tree corridor (2026-11-05, DECISIONS 601 e)
Every emission (ghost and Build) also emits the vanilla bulldoze definition for each tree in the line's corridor
(`LiftLineToolSystem.Trees.cs`, rule `LiftTreeCorridor`): the ghost shows the cut, the Build applies it with the line, a refusal keeps
every tree. Details and logs: EngineNotes/LiftTreeCorridor.md.

## Bend at a loose-end joint (2026-11-02, owner verdict: warn, never refuse)
The tool's line is straight, so it can make a plan bend only where it joins a built line at a loose end. `WarnBend` (every plan): for
each LooseEnd end, the built lift-line edge at that node (no Owner, not Temp / Deleted), its far node, the loose end and this line's
other end go through `LiftLineInput.JoinBendWarnDeg` (LiftAlignMath.PlanBendDeg, 0 at or under BendWarnDeg). Over it the tooltip's
notice slot keeps `SkiResort.LiftTool.BEND` standing ({DEG} with one decimal, rounded up like the hub: `Deg` =
LiftCensus.BendWarnTenths / 10, unit floatSingleFraction) after SUNK and before OPEN_END, and each plan logs one `BEND` WARN line.
Build stays allowed. Bends on built lines (net tool, moved nodes) are the hub's LiftBend row (LiftCensus.md, Plan bends at towers).

## Section delete by the rope (2026-11-03, DECISIONS 591, F1)
The vanilla bulldozer tests a lift edge only on its sheet, EdgeGeometry + the composition's m_SurfaceHeight.max (Game.Net/
RaycastJobs.cs:437-460), never the rope lanes: 9.9 m above the rope mid-span on a 473 m tram span, about 13 m at 1 km
(docs/research/tram-bulldoze-hitbox-2026-11-03.md). So in the first pick step (`LiftLineToolSystem.Sections.cs`) the tool aims at what
the player sees and removes the section under the cursor:
- Rope cache: per lift-line edge (census `CopyLineOwners`, else the tool's edge query) every SubLane with UtilityLane and HV or LV
  UtilityLaneData, its Curve (the hanging curve, LaneSystem.cs:4128-4138) as a polyline (`LiftRopePick.AddRope`, 10 m steps, 8 to 64
  segments). Rebuilt when the census rebuilt and at least every 120 frames while picking; never per frame.
- Pick (`LiftRopePick.Pick`, pure, LiftBatch RP-1..RP-9): the cursor line is the tool raycast's own
  (`ToolRaycastSystem.CalculateRaycastLine`, ToolRaycastSystem.cs:69-78); per rope the closest polyline point; it counts within
  max(1 m, camera distance x tan(fov / 2) x 0.025), about 13 px at 1080p (vanilla lanes use 1 %, RaycastJobs.cs:793, :813-816), and
  not behind the terrain hit. Best miss / tolerance wins; within 0.15 of it the rope nearer the camera wins. Boxes cull first: 20 of
  8000 segments tested, about 0.1 ms (RP-9). The type filter is WantType (a hub tile limits it to that type).
- Fallback: no rope near, the vanilla Net raycast of the previous frame on the same line (its own RaycastSystem context,
  AddInput / GetResult, RaycastSystem.cs:770-803; TypeMask.Net, OnGround | Overground as the bulldozer, BulldozeToolSystem.cs:1460-1469;
  layer PowerlineHigh, matched by ConnectLayers, RaycastJobs.cs:562-563; a node hit on an edge's end goes to that edge,
  BulldozeToolSystem.cs:1656-1658). It only names a section the rope cache holds.
- Priority: a loose end, or a station that can still take a line, keeps its click (the redraw starts there); a station that already has
  its line yields to the rope.
- Delete: the definition BulldozeToolSystem.AddEntity writes for an unowned edge (BulldozeToolSystem.cs:731-780): CreationDefinition
  {m_Original = edge, CreationFlags.Delete} + Updated + a NetCourse of the edge's curve and its two nodes (m_FixedIndex -1, no
  OwnerDefinition). GenerateEdgesSystem makes the Temp with TempFlags.Delete (GenerateEdgesSystem.cs:1371-1376): the game's own
  delete preview, standing while the section is hovered (Clear + destroy + emit only when the hover changes). A click applies once
  that Temp exists: base `GetAllowApply` (no Error, original not deleted; ToolBaseSystem.cs:533-540), applyMode Apply for one frame and
  the definition destroyed, as BulldozeToolSystem.Apply (:1618-1640). ApplyNetSystem deletes the edge and pays the refund
  (CostSystem.cs:189, ApplyNetSystem.cs:662-664); both end nodes stay with their other sections, so the gap is redrawn from its two
  loose ends. No preview within 30 frames of the click, or a refusal: `SECTION_REFUSED` notice and a WARN.
- Overlay: the hovered section's ropes in red (width a quarter of the tolerance); the tooltip shows `SECTION` (type and the
  horizontal length, as the plan line counts a span).
- Neighbors: like the bulldozer's delete, the sections at both end nodes get Temp copies and are validated, so a solid object on
  them refuses the delete; the carriers and their grips are passable while the tool runs (Carriers passable, above).
- Logs: `LiftLine [section] rope cache ...` on change, one `hover` line per new section (miss, tolerance, camera distance, rope point;
  or "by the vanilla sheet fallback"), 40 in full then every 25th, and one `DELETED` line per removal.

## Logs
Silence is impossible: ARMED / patch lines per load; one line per activation, station pick, plan (towers, member mix, spans, clearance,
pins), edit, build (courses emitted), apply (Temps, errors, registered tool line). An exception disables the tool until the next load (one
"!!!" ERROR with the stack).

## Type layers (FIX-LIST 60 a, 2026-11-05)

Why two lifts joined where they crossed: CourseSplitSystem.MergePositions keeps a crossed edge as a course position, so the engine
splits it and both lines share a node, only when `flag4 = !IsOver(...) && CanConnect(...)` (1.6.0f1 CourseSplitSystem.cs:2310-2312;
1.6.2f1 about +40 lines). The private CanConnect ends in NetUtils.CanConnect (NetUtils.cs:499-506): one side's m_RequiredLayers
inside the other's m_ConnectLayers. Power-line layers come from ElectricityConnection (NetInitializeSystem.cs:2414-2454): high
voltage = PowerlineHigh in both masks, so every lift crossed every lift with a join. When CanConnect is false the crossing point is
optional (m_CoursePos.m_Entity = Null, m_IsOptional, :2462-2477): the crossed edge is untouched, and the extra optional points are
dropped while the merged span stays under 0.95 x m_ElevatedLength (FindBestIntersectionToRemove :3200-3216).

SetTypeLayers (load, Game mode) ORs bit 24 + the SkiCatalog.Lifts index into both masks of each line prefab, PowerlineHigh kept.
Different types fail CanConnect; the same type, the station stub marker and city high-voltage lines still connect (their
RequiredLayers is PowerlineHigh alone, inside every lift's ConnectLayers). Readers that test only PowerlineHigh stay as they were:
ConnectionWarningSystem.cs:2363-2367, ElectricityUIUtils.cs:12-43, RaycastJobs.cs:562-563. Vanilla net-tool snapping uses
CanConnect too (NetToolSystem.cs:527, 718, 1043), so it no longer snaps one type onto another's node.

Open (to verify in game): net-vs-net validation is 3D (ValidationHelpers.cs:162-199): two edge sheets whose boxes (EdgeGeometry +
the composition m_HeightRange) intersect raise OverlapExisting; only clearance boxes, NotEnoughClearance. The lift compositions'
height range, so the vertical separation a crossing needs, is not read yet.
