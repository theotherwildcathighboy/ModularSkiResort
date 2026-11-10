# LiftLineToolSystem: how the lift-line tool plugs into the net pipeline and emits a fitted line

`LiftLineToolSystem` is our own lift-line tool. The node set of the line is the planner's plan. The player does these steps:

1. Click the base station.
2. Click the top station.
3. See the planned towers in an overlay preview.
4. Drag, add or remove towers. The planner re-plans around the user's pins.
5. Build.

On Build, the tool emits the line as one CreationDefinition + NetCourse per planned span. The line goes through the game's own apply path
(validation, cost, ApplyNetSystem). The vanilla net tool stays the fallback (`LiftTowerFitSystem` fits whatever it builds). The hub's
lift-line tiles open this tool with their lift type (`TryActivate(why, type)`). The line prefabs have UIObject m_Group null, so the hub
tile is the only way in. Enter on the default plan builds what quick-place built, with fewer, fitted towers.

## Seam into the engine
Re-derived from the vanilla net tool. Nothing is copied from the ParkManager reference (GPL). Citations are the decompile baseline 1.6.0f1.
- A prefab-less `ToolBaseSystem` (the `SkiGladeToolSystem` skeleton):
  - `base.OnCreate` registers the tool and its Apply / Secondary Apply / Cancel proxies (ToolBaseSystem.cs:292-316).
  - `TrySetPrefab` returns false (ToolSystem.cs:263-277).
  - `LiftLineHotkeySystem` activates the tool through `ToolSystem.activeTool` (ToolSystem.cs:84-98).
  - The raycast is terrain only (`InitializeRaycast`).
- For each span, the tool writes one entity `CreationDefinition{m_Prefab = the line net, SubElevation}` + `NetCourse` + `Updated`
  through the ToolOutputBarrier's ECB. This is the shape of `NetToolSystem.CreateStraightLine` (NetToolSystem.cs:3603-3660):
  - StraightCurve.
  - IsFirst on the first start, IsLast on the last end.
  - IsLeft | IsRight everywhere.
  - m_FixedIndex -1, m_ParentMesh -1.
  - Rotation `NetUtils.GetNodeRotation(tangent)`.
- The station ends carry `CoursePos.m_Entity` = the station's stub node. This is the node vanilla snaps to (GenerateEdgesSystem.cs:1242-1248,
  `NodeMapKey` m_Original). The inner nodes are plain positions. The two courses that join at an inner node share it bit-for-bit
  (`NodeMapKey` exact float3, GenerateEdgesSystem.cs:59-70). y = ground + E(member), with NO FreeHeight, so
  `CourseSplitSystem.SampleCourseHeight` keeps the y (CourseSplitSystem.cs:1643-1700).

## Span length raise
`CourseSplitSystem` cuts any course longer than `NetGeometryData.m_EdgeLengthRange.max` into equal pieces (CourseSplitSystem.cs:3487-3493).
`ValidationHelpers` flags an edge longer than 1.1 x max (ValidationHelpers.cs:1395-1425). While this tool is open, it raises
`m_EdgeLengthRange.max` and `m_ElevatedLength` of the four line prefabs to the planner's MaxSpanM. (The game sets both from
`m_MaxPylonDistance`, NetInitializeSystem.cs:2180-2181.) The tool restores both values when it closes. This follows the `CursorAimSystem`
precedent: a prefab-entity data write, logged. The vanilla net tool keeps its own split. A permanent raise cut a long line to one engine
node (LiftBatch LP-20).

## Split gate (PlacementFlags.ShoreLine)
`CourseSplitSystem.CheckHeightRange` also cuts a course where its height profile changes elevation class (FreeHeight transition nodes,
CourseSplitSystem.cs:2884-2942). This built tool lines with more edges than planned. While the tool is open, the line prefabs carry
`PlacementFlags.ShoreLine`. This is the one prefab-side gate that skips the cut (`SetSplitGate`: `sampleTerrain && !RequireElevated && !ShoreLine`,
:2884). Without it, the engine cuts a tool course wherever its height profile changes elevation class. The class test is `GetElevationType`
(:2655-2662): 2 / 4 m above the ground at the piece's sides, m_ElevationLimit 4 (NetInitializeSystem.cs:874). Two examples:
- A span with an end tower of Elevation >= 4 m follows the chord where it is above the ground (:1381-1385, :1437-1438).
- Ground steeper than m_MaxSlopeSteepness is filled (:1436-1450).

Other readers of the bit:
- `CalculateElevation` (:3574-3592, only over water >= 4 m deep).
- `ValidationHelpers.CheckSurface` (:1604, the underwater check of OnGround nets is skipped).
- `NetToolSystem` (:6221), only when the vanilla net tool is the active tool.

The tool sets and restores the flag in the same window as the span raise.

Crossings with other HV nets still split. The fit matches a line with extra nodes (`LiftToolLines.MatchSplit`). A mismatch logs the split
points ("engine split points not in the plan").

## Elevation
`CalculateElevation` re-derives each node's Elevation from the terrain beside it (CourseSplitSystem.cs:3549-3647). The fit system then
writes Elevation = (E, E) exactly. It recognizes the line as a tool line through `ToolLines` (`LiftToolLines`). It changes heights only,
with the tool's members, and never moves a node, so a pinned node is never snapped.

## Apply
One frame after the emission, the Temps exist and are validated (CourseSplit PostTool, GenerateNodes Modification1, GenerateEdges
Modification2, Validation ModificationEnd). Then `applyMode = Apply` for one frame when `GetAllowApply` holds (no Error,
ToolBaseSystem.cs:533-540), and the tool destroys the definitions (`DestroyDefinitions`, :506-518). On errors, the tool does Clear, goes
back to editing, and logs a WARN with the count. Cost is the vanilla Temp pricing (Game.Net/CostSystem).

## Edit loop
The "generate, edit, build" pattern. `LiftLineEdit` holds the model. The input rules are the pure `LiftLineInput`, tested in LiftBatch.
- Left click = the first end, then the other end. An end is one of these:
  - A station's stub. The tool refuses a station that already has its line. To rework it, bulldoze the section, then click its loose ends.
  - A loose end of a lift line of the same type (a node with one lift-line edge and no station). A bulldozed section leaves this, so a
    section is redrawn between its two neighbors.
  - Open ground (a fresh end node), once the type is known (hub tile, or the first end).
- Left click on the line = add a tower (pinned). Press on a tower and drag = move it. Release it off the line = remove it.
- Right click = step back one action: cancel a drag, undo the last edit, drop the second end, drop the first end. Right click closes the
  tool only when nothing is left.
- Build key (Enter by default) = build.
- Esc = the frontend's Back, which closes the tool. It is unverified in game whether Esc also fires the tool's Cancel action. If it does,
  Esc steps back.
- Right click is the tool's Cancel action ONLY. Secondary Apply stays off, because both are the right mouse button by default, and with both live,
  one click removed a tower AND dropped the line.

## Preview
The tool draws into `OverlayRenderSystem.Buffer` at `ToolUpdate`. The buffer is cleared every frame (OverlayRenderSystem.cs:738-790).
- `LiftPlanDraw` colors the planned rope by the lowest carrier's floor margin: green >= 2 m, amber 0-2, red below the floor, violet above
  the max rope height.
- Every tower gets a ground ring colored by its spans:
  - Green = clear.
  - Red = a span below the floor = SHORT.
  - Violet = a span above the max rope height = ROPE TOO HIGH.
- A pinned tower also gets a blue inner ring. Every tower gets a cross-bar at rope height, sized by the member.
- The hovered tower is white. The dragged tower is a white ring at the cursor.

The plan line and the hovered tower's height are in the tooltip (`LiftLineTooltipSystem`). Span lengths and member mix stay in the log.

## Ghost preview (2026-10-26)
In Edit, the tool keeps the plan's courses standing as the game's own Temp preview. The preview shows the translucent cable and tower
models at their planned heights, with the game's error icons. It follows the vanilla pattern (NetToolSystem.cs:6867-6916, ObjectToolSystem.cs:4041-4118):
- On unchanged frames, applyMode is None and the Temps stay (ToolOutputSystem.cs:22-30).
- When the plan's `Revision` changes, the tool does Clear + DestroyDefinitions + re-emit in one frame. The game recycles the old Temps
  (GenerateNodesSystem.cs:1397-1404, GenerateEdgesSystem.cs:675-680).

Definitions without `Updated` generate nothing more (GenerateNodesSystem.cs:2030-2041). CleanUpSystem strips Updated (CleanUpSystem.cs:24-32).
Temps render on MeshLayer.Outline (BatchInstanceSystem.cs:446-459, 589-609). Tower sub-objects copy the owner's Temp flags
(SubObjectSystem.cs:2832-2841). ValidationSystem checks only Temp + Updated (ValidationSystem.cs:1917, 1926-1930), so errors cost one pass
per re-emit.

The tool hides the ghost while a tower is dragged. It clears the ghost on leaving Edit (the pick branch). Build clears the ghost and emits
afresh (state Emitted, the old apply / refuse path). A refusal returns to Edit, and the ghost comes back with its icons. Census, chair pins
and the fit already handle Temp lift nets (the vanilla net tool makes the same Temps). Park Manager has no Temp ghost (overlay only). Its
build is our emitted-then-apply path. Logs: `LiftLine [ghost]` (the first three previews, then every 25th).

### Ghost cleanup on stop (2026-10-26)
Every way out (Esc, another tool, the hub, a load) changes `ToolSystem.activeTool`. The next `ToolUpdate` disables our tool and updates it
once, so `OnStopRunning` runs (ToolSystem.cs:312-318). A load gets there through PreDeserialize, with activeTool = default
(ToolSystem.cs:610-614).

Vanilla does NOT clear on stop:
- ToolBaseSystem.OnStopRunning has no Temp code (:333-339).
- NetToolSystem has no OnStopRunning.
- Its Temps go only when the NEXT tool's first update sets applyMode Clear (m_ForceUpdate, ToolBaseSystem.cs:325-330, DefaultToolSystem.cs:973-980).
  The reason: ToolSystem.applyMode is the last tool's (ToolSystem.cs:147-155), and ToolOutputSystem runs the ClearTool phase from it
  (ToolOutputSystem.cs:22-30).

We do not rely on that. `OnStopRunning` destroys the standing definitions. When a ghost or a Build emission was standing, it also runs the
game's own `ToolClearSystem` itself (`ClearOwnTemps`). This is the same ClearEntitiesJob as vanilla's Clear (Deleted on every Temp, Hidden
originals shown again, ToolClearSystem.cs:63-113). Its commands go on the ToolOutputBarrier, which plays back later in the same frame
(UpdateAfter ToolUpdate, SystemOrder.cs:695). The stop update runs before the new tool's update, so every Temp then is ours. A second clear
by the next tool is a no-op. With no Temp, the system does not run (RequireForUpdate, ToolClearSystem.cs:219). `OnGameLoadingComplete` also
resets the ghost fields. Log: `LiftLine [ghost]: preview cleared on stopped (ToolClearSystem run by the tool itself)`. LiftBatch cannot test
this (no ECS world). The owner test step is in docs/research/lifts/lift-pass-2026-10-26.md section 10.

## Player text (2026-10-26)
Every tooltip and notice is a LocaleEN `SkiResort.LiftTool.*` key. The tool reads it through `LocalizedString.Id(key, args)` (numbers as
LocalizedNumber<int> "integer"). The keys come from `LiftLineInput.PromptKey` / `PlanKey` / `TowerKey` and from `LiftLineToolSystem.T` /
`RefusedNotice` / `RefusedBuild` (REFUSED[the error type the game flagged most]). The log keeps the detail (`Describe`, `DescribeErrors`).
LiftBatch LP-31 gates the keys. It checks that:
- every shown key exists,
- every text has at most 25 words,
- no text has an em dash,
- no text has log syntax,
- every text uses only the ARGs the code passes,
- no key is stale.

## Refusal diagnosis (2026-10-26)
A refused Build (GetAllowApply false: the `Error` query, ToolBaseSystem.cs:313, 533-540) always logs every flagged entity:
- edge / node / object, prefab, Temp flags and original,
- along / lateral on the line,
- each icon's error type (IconElement -> icon PrefabRef -> ToolErrorData.m_Error, via ValidationSystem.AddIcon, ValidationSystem.cs:1747-1752)
  with the Target the icon names.

The warning and the tooltip also show a per-type summary. The log counts Temp edges in two groups. Created edges have TempFlags.Create (the definition had no original). Existing edges touched at
an end are a Temp copy with m_Original (GenerateEdgesSystem.cs:1371-1414). Field test 2's
"planned 8 / Temp 9" was the loose end's existing edge.

The plan log prints the max edge slope against the prefab's limit, and any span TOO STEEP. TOO STEEP shows red in the preview: the tower
rings and, via MarkSteepSpans, the span's segments. So a tower-free station-to-station span shows it too. The tool plans with the line
prefab's live m_MaxSlopeSteepness (NetGeometryData, `With(maxEdgeSlope:)` at the pick). PatchSpans logs each line prefab's live value
against the planner's default.

A station end whose carriers leave below the ground (EndCarrier < -0.25 m, `LiftLineInput.SunkKey`) logs SUNK STATION on every plan. It also
shows one standing tooltip notice:
- SUNK for a picked station.
- SUNK_END for a ground end (the station that will finish it would stand on the lower ground ahead: past a crest, FIX-LIST 55).
- It never applies to a loose end.

A tower whose rope bends past its sheave train's cap (`LiftTowerPlan.IsOverCap`) is drawn red. The plan line reads PLAN_TRAIN. Hovering the
tower reads TOWER_TRAIN (bend and cap). The plan line is green only for a sound plan (FIX-LIST 55). End ropes (`EndRope`):
- A picked station reads its lowest HV rope anchor.
- A loose end uses the node formula.
- A ground end uses the anchor of the station that will finish it. The anchor is over the terrain at that station's lot front (`LiftLineInput.GroundEndRope`,
  LiftLineEdit.md "Ground ends", FIX-LIST 48).
- A ground end also samples the ground that station will leave (`LiftLineInput.PadSampler`, banks-pass 2026-11-09). A pad-cutting station
  holds the ground at its plane out to `PadReachM` past the stub tip. Bases: 12 m. Tops and the tram base: 0. The value comes from
  `StationDef.PadBackLocalZ`. An 8 m linear apron then blends back to the raw ground (the shader blend is [I]). The pad never fills.
  The FG top and the tram top keep the slope and read raw ground. Before, every pad-cutting base ground end on a slope read a false
  SUNK_END (EI-55 pad).

## Carriers passable while the tool runs (2026-11-03, DECISIONS 582)
Two cases draw a line to an existing tower: the redo of a bulldozed middle section, and an extension from a loose end. In both cases the
touched edges, their end nodes and those nodes' towers get Temp copies. ValidationHelpers walks each permanent chair to its top-level owner, an edge that is not
overridable. So a chair inside a Temp tower's bounds draws OverlapExisting (field/2026-11-02/lifticon: a Temp FG12 against two parked
chairs about 2 m from it). Moving chairs ride through every tower, and a broken line parks them where they stand. So the refusal depends on
where chairs stopped.

`SetCarriersPassable` puts GeometryFlags.Marker on the carrier prefabs (an empty collision mask, ObjectUtils.GetCollisionMask) for the whole
tool session (OnStartRunning). It hands back to the station window (`m_StationPlacing`) in OnStopRunning. SetStationPlacing touches the
carriers only while the tool is not running. While the flag is set, carriers also skip the raycast and camera collision. Rendering is
unchanged.

Since the carrier grip round, a carrier is two objects: the body (a carrier, `CarrierGripTops`) and its grip (`GripTypes`: SkiLiftChair4Grip,
SkiTramCarriage, ...), which is not a carrier. The grips get the flag too. Without it, the bridge check of the section delete (2026-11-03,
F1) was refused next to riding chairs. The error was OverlapExisting on the grips against the neighbor sections' Temp copies. Examples: 12
SkiLiftChair4Grip on one FG section, a SkiTramCarriage on the tram's 46 m span. Gate: check-lift-align CARRIERPASS (4 rules, the last one the grip list).

**The window stays the whole session (2026-11-05, DECISIONS 601 e):** the redo needs the window whenever Temp copies of existing towers are
validated. That is every ghost re-emit in Edit (the ghost stands for the whole Edit state) plus the Build. Only the two pick steps have no
Temps. Toggling prefab flags at each pick / edit change would buy nothing. The window has a side effect: trees were not hidden by chairs
during a tool session, but were hidden outside it. This was half of the pop-in the owner saw (note 8). The tree corridor
(EngineNotes/LiftTreeCorridor.md) removes the trees in the carrier sweep, so the Marker state no longer changes what a player sees.

## Tree corridor (2026-11-05, DECISIONS 601 e)
Every emission (ghost and Build) also emits the vanilla bulldoze definition for each tree in the line's corridor
(`LiftLineToolSystem.Trees.cs`, rule `LiftTreeCorridor`). The ghost shows the cut. The Build applies it with the line. A refusal keeps every
tree. Details and logs: EngineNotes/LiftTreeCorridor.md.

## Bend at a loose-end joint (2026-11-02, owner verdict: warn, never refuse)
The tool's line is straight, so it can make a plan bend only where it joins a built line at a loose end. `WarnBend` runs on every plan.
For each LooseEnd end, the built lift-line edge at that node (no Owner, not Temp / Deleted) gives its far node. The far node, the loose end
and this line's other end go through `LiftLineInput.JoinBendWarnDeg` (LiftAlignMath.PlanBendDeg, 0 at or under BendWarnDeg).

Over BendWarnDeg, the tooltip's notice slot keeps `SkiResort.LiftTool.BEND` standing after SUNK and before OPEN_END. The {DEG} value has
one decimal and rounds up like the hub (`Deg` = LiftCensus.BendWarnTenths / 10, unit floatSingleFraction). Each plan logs one `BEND` WARN
line. Build stays allowed. Bends on built lines (net tool, moved nodes) are the hub's LiftBend row (LiftCensus.md, Plan bends at towers).

## Section delete by the rope (2026-11-03, DECISIONS 591, F1)
The vanilla bulldozer tests a lift edge only on its sheet: EdgeGeometry + the composition's m_SurfaceHeight.max (Game.Net/
RaycastJobs.cs:437-460). It never tests the rope lanes. The sheet sits 9.9 m above the rope mid-span on a 473 m tram span, and about 13 m
above it at 1 km (docs/research/tram-bulldoze-hitbox-2026-11-03.md). So in the first pick step (`LiftLineToolSystem.Sections.cs`), the tool
aims at what the player sees and removes the section under the cursor:
- Rope cache: for each lift-line edge (census `CopyLineOwners`, else the tool's edge query), the tool takes every SubLane with UtilityLane
  and HV or LV UtilityLaneData. It takes the Curve of each (the hanging curve, LaneSystem.cs:4128-4138) as a polyline (`LiftRopePick.AddRope`,
  10 m steps, 8 to 64 segments). The tool rebuilds the cache when the census rebuilt, and at least every 120 frames while picking. It never
  rebuilds per frame.
- Pick (`LiftRopePick.Pick`, pure, LiftBatch RP-1..RP-9):
  - The cursor line is the tool raycast's own (`ToolRaycastSystem.CalculateRaycastLine`, ToolRaycastSystem.cs:69-78).
  - Per rope, the tool takes the closest polyline point. The point counts when both of these are true: it is within max(1 m, camera distance x tan(fov / 2) x 0.025), and it is not behind the
    terrain hit. The tolerance is about 13 px at 1080p (vanilla lanes use 1 %, RaycastJobs.cs:793, :813-816).
  - Best miss / tolerance wins. Within 0.15 of that value, the rope nearer the camera wins.
  - Boxes cull first: 20 of 8000 segments tested, about 0.1 ms (RP-9).
  - The type filter is WantType (a hub tile limits it to that type).
- Fallback: when no rope is near, the tool uses the vanilla Net raycast of the previous frame on the same line.
  - It has its own RaycastSystem context (AddInput / GetResult, RaycastSystem.cs:770-803).
  - TypeMask.Net, OnGround | Overground as the bulldozer (BulldozeToolSystem.cs:1460-1469).
  - Layer PowerlineHigh, matched by ConnectLayers (RaycastJobs.cs:562-563).
  - A node hit on an edge's end goes to that edge (BulldozeToolSystem.cs:1656-1658).
  - It only names a section the rope cache holds.
- Priority: a loose end, or a station that can still take a line, keeps its click (the redraw starts there). A station that already has its
  line yields to the rope.
- Delete: the tool writes the definition that BulldozeToolSystem.AddEntity writes for an unowned edge (BulldozeToolSystem.cs:731-780).
  The definition is CreationDefinition {m_Original = edge, CreationFlags.Delete} + Updated + a NetCourse of the edge's curve and its two nodes
  (m_FixedIndex -1, no OwnerDefinition). GenerateEdgesSystem makes the Temp with TempFlags.Delete (GenerateEdgesSystem.cs:1371-1376). This is
  the game's own delete preview. It stands while the section is hovered (Clear + destroy + emit only when the hover changes). A click
  applies once that Temp exists and base `GetAllowApply` holds (no Error, original not deleted, ToolBaseSystem.cs:533-540). Then applyMode is
  Apply for one frame and the tool destroys the definition, as BulldozeToolSystem.Apply (:1618-1640). ApplyNetSystem deletes the edge and
  pays the refund (CostSystem.cs:189, ApplyNetSystem.cs:662-664). Both end nodes stay with their other sections, so the gap is redrawn from
  its two loose ends. If no preview exists within 30 frames of the click, or on a refusal, the tool shows a `SECTION_REFUSED` notice and
  logs a WARN.
- Overlay: the hovered section's ropes draw in red (width a quarter of the tolerance). The tooltip shows `SECTION` (type and the horizontal
  length, as the plan line counts a span).
- Neighbors: like the bulldozer's delete, the sections at both end nodes get Temp copies and the game validates them. So a solid object on
  them refuses the delete. The carriers and their grips are passable while the tool runs (Carriers passable, above).
- Logs:
  - `LiftLine [section] rope cache ...` on change.
  - One `hover` line per new section (miss, tolerance, camera distance, rope point, or "by the vanilla sheet fallback"). The log prints 40
    in full, then every 25th.
  - One `DELETED` line per removal.

## Scripted entry (Resort Planner R1-c)
- `TryActivateFor(why, type, baseStation, topStation, autoBuild)` and `TryActivateForGround(why, type, a, b, autoBuild)` open the tool
  for one line. They return a `ScriptTicket`. The caller reads `Outcome` every frame.
- The picks run in the next update of the tool (`UpdateScript`, then `RunScript`). They use the same `PickEnd` as two clicks. A
  station end comes from the transform and the catalog row of the station, because a fresh station may not be in the census yet.
- With auto build, the tool calls `RequestBuild` when `LiftTowerPlan.QueueRule` allows the plan and no ALONG, CROSS or BACKWARD
  notice stands (`AutoBuildBlocker`). Otherwise the tool waits in Edit for the player.
  - QueueRule (task R2-l, DECISIONS 637): a Sound plan builds. A plan with every span clear, exactly one tower over its sheave-train
    limit, no tower too steep and no tower past its clearance also builds. This is the build that Enter makes.
  - Two or more towers over the limit wait as PLAN_TRAINS. Any other fault waits on its plan key (`LiftTowerPlan.QueueWaitKey`).
  - The ticket keeps `OverLimitTowers` and the first such tower: `OverLimitTower` (its place from the base), `OverLimitAlongM`,
    `OverLimitBendDeg` and `OverLimitCapDeg`. The picks set them, and the apply sets them again from the plan that was built.
  - Log: `LiftLine [script]: #N picked — ..., one tower over its sheave-train limit (tower T at A m bends B deg, its limit is C), no other
    defect: build requested (as Enter builds it)`.
- Outcomes:
  - Pending: queued, or picked with a build requested.
  - Waiting: in Edit with both ends picked. `Reason` is the plan key, the notice, or the refusal of the game.
  - Built: the line is permanent. `Line`, `Towers`, `LengthM` and the over-limit fields are set.
  - Refused: the tool could not open, or could not pick the ends.
  - Abandoned: the tool closed, the ends were dropped, or another scripted line took its place.
- A ticket that waits can still end Built when the player builds the line.
- The tool: a scripted line that ends Built gives the tool back to the default tool (task R1-e). In the field r1c pass the tool stayed
  open after the queue (note 4). This includes a Waiting line that the player builds. A Waiting line keeps the tool open, because the player fits it.
  `IdleAfterScript` is true when the tool is active with no end picked, no preview and no open ticket. The build queue reads it when it
  ends (`ResortLiftBuildSystem.HandToolBack`).
- Logs: `LiftLine [script]` per ticket start, pick, WAITING and end.

## Logs
Silence is impossible. The log has ARMED / patch lines per load. It has one line for each of these:
- activation,
- station pick,
- plan (towers, member mix, spans, clearance, pins),
- edit,
- build (courses emitted),
- apply (Temps, errors, registered tool line).

An exception disables the tool until the next load (one "!!!" ERROR with the stack).

## Type layers (FIX-LIST 60 a, 2026-11-05)

Why two lifts joined where they crossed: CourseSplitSystem.MergePositions keeps a crossed edge as a course position only when
`flag4 = !IsOver(...) && CanConnect(...)` (1.6.0f1 CourseSplitSystem.cs:2310-2312, 1.6.2f1 about +40 lines). Then the engine splits the
edge, and both lines share a node. The private CanConnect ends in NetUtils.CanConnect (NetUtils.cs:499-506). It tests that one side's
m_RequiredLayers are inside the other's m_ConnectLayers. Power-line layers come from ElectricityConnection (NetInitializeSystem.cs:2414-2454).
High voltage = PowerlineHigh in both masks. So every lift crossed every lift with a join. When CanConnect is false, the crossing point is
optional (m_CoursePos.m_Entity = Null, m_IsOptional, :2462-2477). The crossed edge is untouched. The engine drops the extra optional points
while the merged span stays under 0.95 x m_ElevatedLength (FindBestIntersectionToRemove :3200-3216).

SetTypeLayers (load, Game mode) ORs bit 24 + the SkiCatalog.Lifts index into both masks of each line prefab. PowerlineHigh stays. Different
types fail CanConnect. The same type, the station stub marker and city high-voltage lines still connect (their RequiredLayers is
PowerlineHigh alone, inside every lift's ConnectLayers). Readers that test only PowerlineHigh stay as they were:
- ConnectionWarningSystem.cs:2363-2367
- ElectricityUIUtils.cs:12-43
- RaycastJobs.cs:562-563

Vanilla net-tool snapping uses CanConnect too (NetToolSystem.cs:527, 718, 1043), so it no longer snaps one type onto another's node.

Net-vs-net validation is 3D (ValidationHelpers.cs:162-199, segment-segment :3316-3370, QuadIntersect :3429). Each edge is a box from its
EdgeGeometry up by the composition m_HeightRange. The EdgeGeometry is the hanging curve. NetUtils.StraightCurve lowers the inner control
points by d h 4/3, so the curve sits 4 d h t (1 - t) under the node chord. h = the line prefab's m_Hanging = the rope lane SagRatio. The
validation compares the boxes over the whole overlap of the two strips. An overlap refuses the build ("Clearance too low" in the field run
2026-11-05 run3-crossing-proof-12d5ff6). Height ranges (collision box round, 2026-11-05): FG 4..8.5, DQ 3.8..9.5, GD 4..11.5, tram
11.75..27.5 m. They run about 1 m under the carriers to 1 m over the rope (LiftTowerPlanConstants.NetHeightRange). The old values were
FG / GD 6..20, DQ 5.8..20, tram 13.75..40.

Crossing rule (DECISIONS 602). CollectCrossings (each replan) sorts the drawn line's crossings with built lift edges. Every crossing, of any
type, becomes a LiftPlanCrossing for the planner. History:
- Until the 2026-11-05 collision box round, the same type joined (CROSSES).
- With the tight box, CourseSplitSystem IsOver / IsUnder (:2119-2185) judge a same-type line a few meters higher as over, with no join.
  Validation over the whole overlap still finds an overlap: "Clearance too low".
- Planned over with the box rule (78e0ff5), the game still split the drawn course beside the crossing, unjoined (same layer: a CourseSplit
  candidate). The fit then stood a second tower there.
- So the tool refuses a same-type crossing before planning (LiftCrossRefusal.SameType, CROSS_SAME_TYPE).

For each crossing, CollectCrossings records:
- along,
- the crossed curve's height and slope at the crossing (MathUtils.Position / Tangent at t),
- the angle,
- the crossed composition's width and height range (NetCompositionData, logged once per type as `LiftLine [cross]`, match or MISMATCH against
  the constants),
- the distance to its nearest tower and station node (IsStationNode).

The planner (LiftTowerPlanner.md, Crossings) passes over or under, or refuses. WarnCrossing logs one `LiftLine [plan]: CROSS ...` line per
crossing. LiftLineInput.CrossKey picks the notice. A refusal (CROSS_SHALLOW / STATION / TOWER / OWN_TOWER / CLEARANCE) gives a standing
notice, and Enter is refused, like ALONG. Otherwise the notice is CROSSES_UNDER / OVER.
