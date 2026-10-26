# LiftTowerPlanner: the planner's ground model and phases, the engine facts behind a fitted node, and the tram's 3 km span

`LiftTowerPlanner` is the pure tower planner for a lift line: no ECS, no Game types, LiftBatch tests it headless (`LiftPlannerTests`). It
runs once per edit (or once per printed diag block), so it is plain C#, not Burst; arrays are reused per planner instance. Deterministic:
no randomness, no hash order, ties broken by the sample index. Citations are the decompile baseline 1.6.0f1.

## Input
- A terrain sampler (along-line meters from station A, lateral meters; + = right of A to B), sampled every StepM along the line at the
  center and at +-HalfWidthM (the engine's own side samples, CourseSplitSystem.cs:3566-3573, half the net width).
- The line length (xz) and the two station anchors' rope heights (world y of the haul rope at the stub).
- `LiftTowerPlanConstants` for the lift type (ladder members, rope offset, carrier drop, floor, max rope height, spans, width, sag).
- Optionally seed nodes (the output of an earlier plan), kept as anchors: re-planning a plan is a fixed point.

Constants provenance: ladder tops are the tower manifests' top_anchor_y = ObjectGeometryData m_Size.y, confirmed in game (member lists in
the mod log). Rope offset = shortest top - catalog AxleM (lane y on the shortest tower), which equals every member's top - axle in the
manifests (FG 1.974, DQ 1.95, GD 2.0, tram stubs 3.75). Drop = catalog HangM. Half width = piece m_Width / 2 (SkiChairliftPiece /
SkiDetachablePiece 6, SkiGondolaPiece 8.2, SkiTramPiece 15). Sag = the net's m_Hanging (FG net SkiChairliftHV 0.03; DQ / GD / tram rope
lanes 0.004).

## Ground model (per sample)
- center = the ground under the line axis (the tower base: our own spawn stands on it).
- high = max(center, left, right): the clearance ground. The rope must keep the chair floor over the higher side; this is the engine's
  "lower elevation side" (cmin of Net.Elevation, SubObjectSystem.cs:980), so the side that governs the engine's ladder also governs ours. A
  node whose high and low side differ by more than SideHillM is flagged SideHill.
- rope(t) on a span = the straight chord between the two rope points minus the engine's sag: LaneSystem.cs:4128-4134 lowers the bezier's
  b / c by d*m_Hanging*4/3, a parabola 4*d*m_Hanging*t(1-t) (mid-span sag = d*m_Hanging).
- floor margin(t) = rope(t) - CarrierDropM - high(t) - floor(t) (>= 0 required); floor(t) = FloorM, ramped near a station end (Station
  ends below).
- rope height(t) = rope(t) - center(t) (<= MaxRopeAboveGroundM when that is > 0; the tram has no cap).

## Phases
- (a) Anchors: the station-to-station chord (or a seed-to-seed span) that has a violation is split where the terrain forces it. The split is
  solved exactly (`Anchors`: a shortest-path search over the samples x ladder members) instead of a greedy recursive worst-intrusion split
  (which put far too many towers on a 40 m wall): fewest towers (anchors + the infill (b) will add), then fewest anchors, shortest members,
  anchors nearest the worst-intrusion sample. The floor test ignores the sag on spans (b) will split (their sub-spans sag far less) and keeps
  it on spans (b) leaves whole (FloorSag). Anchor spans <= 3 x MaxSpanM. If no clear arrangement exists, the greedy split (`Split`) minimizes
  what is left. Flags: Shoulder (the ground pokes through the chord), Valley (max height), Breakover.
- (b) Infill: a span between anchors gets max(ceil(len / MaxSpanM), round(len / TypicalSpanM)) sub-spans: first at the equal-spacing
  samples, else each infill node slides inside +-half a sub-span of its spot to the clear spot with the shortest member, nearest the spot.
  No sub-span exceeds MaxSpanM or 1.5 x TypicalSpanM - StepM (so a re-plan rounds it to one span: idempotence). If no arrangement is clear,
  one to three extra sub-spans are tried; the best is kept and its violation reported.
- (c) Elevation: every tower starts at the shortest member that keeps the floor at its own node (side-hill); then, while a span has a floor
  violation, the end nearer the worst sample (the other end if that one is at the top of the ladder or is a station) goes up one member.
  Members whose rope is above MaxRopeAboveGroundM are never used. If (c) leaves a violation that the (a) / (b) members did not have, the
  (a) / (b) members are kept.
- (d) Output: ordered nodes (along, ground, high side, member, rope y, flags) plus the per-span clearance profile; `AppendLog` writes a
  "Towers [plan]" block in the "Towers [nodes]" format of `LiftTowerDiagSystem` so the two compare line by line.

### Breakover rule
Fixed-grip lines put a tower just before a breakover (a convex change of slope) so the rope follows the profile. So a Shoulder on convex
ground becomes a Breakover: it moves one sample past the crest toward the higher ground (uphill of the crest, not onto it) with the shortest
member that keeps both spans clear and adds no infill tower. `MinTowers` prefers the most towers on the uphill sample of a convex crest
(`Breaks`), never at the cost of an extra tower.

## Station ends, the station-side rule and the edge slope (2026-10-26 lift pass)
Report: docs/research/lifts/lift-pass-2026-10-26.md. Tests: LiftBatch LP-26..LP-29.
- **Approach floor.** Carriers leave a station at loading height (DQ: bullwheel rope 3.70 m above the platform, hang 3.30 m = 0.40 m), so
  the floor at a station end ramps linearly from the carrier's own height at the end (0, or negative at a sunk station) to FloorM at
  StationClearM (FG 12.2, DQ 25.6, GD 25.4, tram 40 m): `LiftTowerPlanConstants.ApproachFloor`. Before, FloorM applied from the first
  sample and forced a tall tower right at every station via the NoClearance fallback (field DQ: DQ12 @10 m and DQ18 @1,015 m). Only ends
  the caller marks as stations get it (`StationA` / `StationB`: the tool from its end kinds, the fit from the rope lanes it found); a
  loose or ground end is a tower node with the full floor.
- **Station-side rule.** `MinCost.EndRiseM` = the rise above the shortest member of each tower next to a station end. With
  `TowerWorthM` > 0 (FG / DQ / GD 8 m) the search minimizes towers x TowerWorthM + EndRiseM first: one more tower buys 8 m of shorter
  station towers (ordinary 1,200 m DQ slope: DQ18 @105 / DQ20 @1,075 with 5 towers -> DQ10 at both ends with 6). The tram has
  TowerWorthM 0 (fewest towers first, EndRiseM breaks ties). `Fit` turns `StationRule` off for its free plan: the count the line needs is
  the fewest towers. A strict station-first order was tried and dropped: it stood FG8s 15-20 m from stations and inflated the fit's count.
- **Edge slope (SteepSlope).** The engine refuses a Temp edge whose node-to-node |dy| / xz length reaches the line prefab's
  m_MaxSlopeSteepness + 0.0005, or half of it 2x that (ValidationHelpers.cs:1435-1490, test :1462; the FlattenTerrain branch at :1435
  does not apply to lift lines). Line prefabs: SkiChairliftHV 0.8, SkiDetachableHV 0.9, SkiGondolaHV 1.2, SkiTramHV 2.5
  (`MaxEdgeSlope`). With `CheckEdgeSlope` (the lift tool only) every MinTowers transition, the breakover move and phase (c)'s raise keep
  |dy| <= 0.97 x MaxEdgeSlope x d between the nodes as built (towers at ground + E(member), the ends at `EndNodeYA` / `EndNodeYB`);
  `TotalViolation` counts the excess; `Build` sets `LiftPlanSpan.EdgeSlope` / `SlopeViolated`. Off for the fit: permanent edges are not
  validated. The legacy Anchors / Split / Infill path has no slope gate.
- **Outputs.** `LiftTowerPlan.SampleFloorM` (MarginAt uses it), `StationA` / `StationB`, `EndCarrierA` / `EndCarrierB` (carrier at the end
  above the high side: negative = a sunk station; the tool warns SUNK STATION), `MaxEdgeSlope`.

## Fixed node count (Fit)
The engine's node count is fixed (equal split, CourseSplitSystem.cs:3485-3493), so `LiftTowerFitSystem` asks `Fit`:
1. `PlanFixed` = phase (c) only at the line's own nodes (each node gets its own profile sample);
2. else, on a straight line, the free plan with spans capped at the engine's max edge length, spare nodes at the longest spans' midpoints;
3. else Short (the better of the two, plus the tower count the line would need).

Hysteresis: the members the line already carries win when they clear it as they stand. Phase (c) re-run on ground that moved a few
decimeters (our own carve, rounding) can pick an equally clear but different assignment (a 1,223 m tram flipped between two member mixes on
every re-fit); a clear line is final. `LiftTowerEdit` holds the engine-side numbers (node elevation per member, the vanilla score /
top-anchored placement replica, the power-line edge curve). Tests: LiftBatch LP-11..LP-15.

## LiftTowerEdit: the engine-side numbers of a fitted node (LP-15)
A tower node is raised so that the vanilla placeholder ladder spawns exactly the planned member on the ground:
- node y = center ground + E, `Game.Net.Elevation` = (E, E), E = TopM[member] - TopM[0].
- `SubObjectSystem` scores member m' with spare = m_Size.y - (cmin(Elevation) + spec y) (SubObjectSystem.cs:980, :2189-2199; spec y =
  TopM[0] in all four line prefabs: FG 9.014, DQ 9.95, GD 12, tram 30). So the planned member has spare 0 and score 1, and every other
  member scores at most 1/(1 + step): no tie, no random draw (:2346-2358). A node tagged Updated is re-scored (owner query All SubObject +
  Any Updated, :3936-3943).
- A Standalone pillar is placed TOP-anchored (AnchorTop + OnGround, :2464-2469): pivot = node y + spec y - member top (:2477-2482; the
  node-y-minus-elevation rule at :2521 skips AnchorTop rows) = ground + E + TopM[0] - TopM[m] = the ground.
- The rope lane = node y + lane y (LaneSystem.cs:4112-4115) = ground + TopM[m] - RopeBelowTopM = the planner's rope.
- The edge curve is the engine's own power-line curve between the two node points, `NetUtils.StraightCurve(a, d, net m_Hanging)`
  (NetUtils.cs:178-191; CourseSplitSystem.cs:1702-1704 for StrictNodes nets).
- A node up to the "above" tolerance over its target is left alone: every write re-curves the node's edges, the carve (cut ceiling = edge y
  + m_HeightRange.min - 2) follows the curve, and on steep tram spans the cut reaches the node, so the ground under it sank 0.39 -> 0.33
  -> 0.27 m per write (ratio ~0.85). A fit that re-grounded on every 0.25 m wrote three times and hit the loop guard.

## Own cut (the trench the fit must not mistake for ground)
A lift line is a FlattenTerrain power line: `TerrainSystem` cuts the terrain down to a ceiling of edge y + NetCompositionData.m_HeightRange.min
+ TerrainComposition.m_MinHeightOffset (`AddSegment`, TerrainSystem.cs:1397-1516; our pieces {6, 20} and -2 = edge y + 4) and never fills. The
cut is live: it is drawn into the height cascade from the base heightmap on every update of the area (`DrawHeightAdjustments`, :4727-4800; the
brush path writes the base map, :3950), and the CPU heights the fit samples are read back from the cascade (:3598-3646).
- A vanilla line drawn on the ground runs its edges up to 15 m under convex terrain, so its cut is meters deep. The fit sampled that as the
  ground and stood towers in the trench (node y = cut floor + E).
- Once the old edges were replaced the trench refilled around the towers (the ground at a fitted tower rose +0.5 / +0.4 / +1.9 m after the
  write), and a node left more than 4 m under the ground keeps its own round cut: pits.
- Rule: a sample that sits at this line's own cut ceiling (within OwnCutTolM; natural ground is never exactly there, the cut never fills) is
  replaced by the plane through the ground just outside the cut (lateral +- (half width + CutFarMarginM)), when that plane is higher. Ground
  below the ceiling (a valley the rope crosses) is the player's and is kept.

## Tram span: 3 km (TramMaxSpanM)
Peak 2 Peak's record span is 3.03 km. Only the lift-line tool builds a span this long: while it is open the line prefab's max edge length is
raised to it (`LiftLineToolSystem.SetSpansRaised`); the vanilla net tool keeps SkiTramHV `m_MaxPylonDistance` 1,000 m. Limits checked for one
3 km edge:
- Course split follows the raised max (CourseSplitSystem.cs:3487-3493). Validation flags a Temp edge longer than 1.1 x max (LongDistance,
  ValidationHelpers.cs:1395-1425), 3,300 m while open. A Temp copy of a built span longer than 1,100 m made while the tool is CLOSED is refused
  (for example relocating a tram station whose line has a long span); permanent edges are never re-validated. Workaround: rebuild the
  section with the lift tool.
- Elevation segmentation (CourseSplitSystem.cs:2884-3040, `m_ElevatedLength`) is gated off by ShoreLine while the tool is open.
  `NodeReductionSystem` merges collinear Temp edges only for nodes with `Temp.m_Original`, so fresh tool nodes never merge.
- Rope sag is linear in length (4*d*h*t(1-t), LaneSystem.cs:4128-4134): 12 m mid-span at 3 km with the tram's 0.004, exact at any length.
  Every span is tested at every 10 m sample with the sag, floor = cabin bottom (rope - 10.5 m drop) >= 5 m over the high side.
- Terrain cut: ceiling = chord + 11.75 for the tram, and a clear plan keeps the ground <= chord + 10.75 - sag, so a clear span never cuts.
- Rope rendering: the vanilla cable lane mesh has `m_TilingCount 0` and `m_GeometryTiling false`, so `BatchDataHelpers.GetTileCount`
  returns 1 (BatchDataHelpers.cs:581-598): one mesh deformed along the whole lane; each chord deviates ~0.6 m from the parabola, invisible.
- Cabin path: `LiftLoopCache.SampleSegments` uses `MathUtils.ClampLength` (16 chords, linear in t inside a chord); the position is always on
  the rope and only the speed mapping is approximate (negligible; the arc table is double).
- After a load the tool registry is gone and `LiftTowerFitSystem` passes engineMax = 1,000: hysteresis keeps a clear line as built; a 3 km
  span that stops clearing (terraformed) is Short.
- The fewest-towers objective hangs a tram over a 1-2 km valley with no tower at all, as real 3S lines do. Planner cost: LP-10 tram 3 km
  rolling 9.9 -> 23.6 ms, LP-25 ridge 43.6 ms for 3 plans; the tool re-plans once per edit, not per frame.

## LiftTowerNumbering
The number on a tower's plate is its height class baked into the prefab texture (`plate_numeral` in the tower build script: FG8 -> 8,
DQ12 -> 12, GD20 -> 20), so a line that mixes members would read 12, 16, 20, 12, ... instead. Order rule: tower n = the n-th line tower by
distance along the line from the base station, 1 at the base, dense (nodes without a tower are skipped). Computed from geometry only, so it
is the same after a save / load; an edit renumbers only the towers after the first changed one. The tile objects themselves:
`TowerNumberTiles.md`.
