# LiftTowerPlanner: the planner's ground model and phases, the engine facts behind a fitted node, and the tram's 3 km span

`LiftTowerPlanner` is the pure tower planner for a lift line: no ECS, no Game types, LiftBatch tests it headless (`LiftPlannerTests`). It
runs once per edit (or once per printed diag block), so it is plain C#, not Burst; arrays are reused per planner instance. Deterministic:
no randomness, no hash order, ties broken by the sample index, and the same plan whichever end the caller passes as A (Direction,
below). Citations are the decompile baseline 1.6.0f1.

## Input
- A terrain sampler (along-line meters from station A, lateral meters; + = right of A to B), sampled every StepM along the line at the
  center and at +-HalfWidthM (the engine's own side samples, CourseSplitSystem.cs:3566-3573, half the net width). Optionally a second
  sampler from B (`terrainFromB`: meters from B, + = right of B to A) for the plans that work from B; without it the planner mirrors
  the first (`terrain(L - s, -l)`, equal within float rounding).
- The line length (xz) and the two station anchors' rope heights (world y of the haul rope at the stub).
- `LiftTowerPlanConstants` for the lift type (ladder members, rope offset, carrier drop, floor, max rope height, spans, width, sag).
- Optionally seed nodes (the output of an earlier plan), kept as anchors: re-planning a plan is a fixed point.

Constants provenance: ladder tops are the tower manifests' top_anchor_y = ObjectGeometryData m_Size.y, confirmed in game (member lists in
the mod log). Rope offset = shortest top - catalog AxleM (lane y on the shortest tower), which equals every member's top - axle in the
manifests (FG 1.974, DQ 1.95, GD 2.0, tram stubs 3.75). Drop = catalog HangM. Half width = piece m_Width / 2 (SkiChairliftPiece /
SkiDetachablePiece 6, SkiGondolaPiece 8.2, SkiTramPiece 15). Sag = the rope lane's m_Hanging = the catalog's Envelope.LaneHanging (FG
SkiFixedGripHaulRope 0.02, DQ / GD 0.025, tram 0.04 since the rope sag round; FG was 0.01 and the others 0.004 before it). Drop =
HangM stays conservative: carriers ride the rope point since F5, GripTopAboveRopeM (0.08 / 0.22 m) higher than HangM below the rope.

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
- (d) Rope angles (breakover / hold-down pairs, tool plans only: `Pairs`, set by `LiftLineEdit.Replan`): a local post-pass after (c).
  The tower whose rope deflection (`LiftAlignMath.DeflectionDeg`, sag h) is furthest past its train's cap goes first, re-evaluated
  after every change, one repair per original tower. Candidates, cheapest first (fewer towers, fewer ladder steps, smaller max
  |deflection|, shorter spacing, nearer the old spot): (1) the tower shifted within AngleShiftM 20 m with any member, or, beside a
  fixed tower (a pin, a seed, the junction end) PairMaxM or nearer, anywhere in that tower's partner window; a fixed tower only
  changes member; (2) a free tower replaced by a pair PairMinM..PairMaxM (10..40 m) apart within 20 m of its spot; a fixed tower keeps
  its spot and (5) pulls its free neighbor in as its partner or (3) gets a new free partner; two fixed towers 40 m or nearer are the
  player's pair, (4) re-membered only. Every candidate keeps its spans clear (sag, edge slope, MaxSpanM), the station clearance and
  the removed spots, both touched towers within their caps, the untouched neighbors no further past theirs, and never leaves three
  towers in a row 40 m or nearer apart (MaxGroup 2). Nothing feasible: the tower stays, flagged OverCap, and the note names the
  refusals per reason. A line end joining a built line is judged against that line's far node rope point (`EndOuterDistA` /
  `EndOuterRopeA`, B likewise, from `LiftLineToolSystem.OuterPoint`). The angle pass judges deterministic work (`AngleWork`,
  rope-angle evaluations: LiftBatch CO-1 bounds it) and reports its wall time (`AnglesMs`): 0.001 ms on an ordinary 1.2 km DQ,
  0.17 ms on a 2 km DQ with three sharp crests. Practical reach found by LiftBatch (BO-1, BO-2): DQ pairs a 28 deg ground chord
  change (0.60 -> 0.05), GD 36 deg (0.90 -> 0.10); FG only about 8.5 deg (its 4 m ladder steps and 90 m max span; the plan
  estimated 13.9), so sharper FG crests stay single and flagged.
- (e) Output: ordered nodes (along, ground, high side, member, rope y, flags) plus the per-span clearance profile, and on every
  judged node (every plan, the fit's included) the deflection, the two load cases, the train kind and the cap (`SetAngles`);
  `AppendLog` writes a "Towers [plan]" block in the "Towers [nodes]" format of `LiftTowerDiagSystem` so the two compare line by
  line, each tower with its angle text ("d +12.3 (tight +9.1 / heavy +17.0) S [pair]", "OVER CAP" when flagged).

### Breakover rule
Fixed-grip lines put a tower just before a breakover (a convex change of slope) so the rope follows the profile. So a Shoulder on convex
ground becomes a Breakover: it moves one sample past the crest toward the higher ground (uphill of the crest, not onto it) with the shortest
member that keeps both spans clear and adds no infill tower. `MinTowers` prefers the most towers on the uphill sample of a convex crest
(`Breaks`), never at the cost of an extra tower.

## Direction: the same plan from either end (planner-direction-2026-11-04)
The phases work in one direction, and every tie and remainder resolves toward the end they start from: the sample grid starts there
and its short last step lands at the other end (`MinGap` counts samples, so the fallback can stand a tower StepM + remainder from the
far end), `SearchMin` keeps the first of equal-cost states, the station rule's one extra tower goes to the first segment, and the local
passes (crest marks, phase (c), (d), `Fit`'s spare midpoints) prefer the lower index. Before 2026-11-04 that end was the player's first
click, so the same line planned differently from each end (91 of 192 synthetic lines; the FG station pair at x -1200 in the B1 bridge
run: 6 towers with a pair from the base, 7 without from the top). Report: docs/research/planner-direction-2026-11-04.md.
- **Rule.** `Plan` works from the end with the lower rope (`PlansFromB`, an exact float compare); equal ropes fall back to the ends'
  world xz (`EndKeyA` / `EndKeyB`: the smaller x, then z; the tool sets them), and with no keys to A. Working from B it mirrors the
  seeds and `Forbidden`, swaps every per-end input (`StationA/B`, `EndNodeYA/B`, `EndOuterDistA/B`, `EndOuterRopeA/B`), plans
  (`PlanCore`, the old body, untouched) and mirrors the plan back (`Mirrored`: nodes, spans, the profile, the per-end outputs);
  the inputs are restored in a `finally`. A deflection reads the same from either side, so the angles are copied. The angle notes
  are written in the caller's frame (`NoteAt` / `NoteNode`; a PAIR note names its towers in the caller's order).
- **Exactness.** The tool's plan is bit-identical both ways: `LiftLineEdit` snaps pins to the working end's grid and passes a sampler
  from B (`PointFromB`, the same world points as the reversed edit's `PointAt`). Without a B sampler (the fit, the diag, direct
  callers) the planner mirrors the ground itself and the plans agree within the snap step with the same members and pairs
  (LiftBatch DR-1).
- **Scope.** `Fit`'s free plan goes through `Plan`, so it is canonical too; `PlanFixed` (the line's own nodes) and the kept path work
  from A as before. `AsGiven` (internal, tests only) replays the old behavior (LP-26's pre-pass field plan, the DR sweeps' binding
  checks).
- **Quality and cost.** The rule picks one of the two old plans, it adds no planning pass: on 825 synthetic lines planned both ways
  by the old planner, 714 differ, 702 of them with the same clearance, flags and tower count; 4 are better from the lower rope, 8
  from the higher (FG / DQ only). A line whose plan from the lower rope leaves a crest flagged OverCap can lose a pair the other
  end found (LiftBatch DR-4: FG crest 402.3 m).

## Station ends, the station-side rule and the edge slope (2026-10-26 lift pass)
Report: docs/research/lifts/lift-pass-2026-10-26.md. Tests: LiftBatch LP-26..LP-30 (LP-30: the review fixes,
docs/research/review-153b64e-2026-10-26.md F3 / F4 / F9 / F10).
- **Approach floor.** Carriers leave a station at loading height (DQ: bullwheel rope 3.70 m above the platform, hang 3.30 m = 0.40 m), so
  the floor at a station end ramps linearly from the carrier's own height at the end (0, or negative at a sunk station) to FloorM at
  StationClearM (FG 12.2, DQ 25.6, GD 25.4, tram 40 m): `LiftTowerPlanConstants.ApproachFloor`. Before, FloorM applied from the first
  sample and forced a tall tower right at every station via the NoClearance fallback (field DQ: DQ12 @10 m and DQ18 @1,015 m). Only ends
  the caller marks as stations get it (`StationA` / `StationB`: the tool from its end kinds, the fit from the end node's Owner, a
  station sub-net stub; every line edge has HV rope lanes at both nodes, so a lane says nothing about a station); a loose or ground end
  is a tower node with the full floor.
- **Station-side rule.** `MinCost.EndRiseM` = the rise above the shortest member of each tower next to a station end. With
  `TowerWorthM` > 0 (FG / DQ / GD 8 m) the search minimizes towers x TowerWorthM + EndRiseM first: one more tower buys 8 m of shorter
  station towers (ordinary 1,200 m DQ slope: DQ18 @105 / DQ20 @1,075 with 5 towers -> DQ10 at both ends with 6), at most ONE tower
  over the fewest per line (`m_ExtraTowers`; DECISIONS 429): a segment touching a station runs the fewest-towers search first, then the
  weighted one, and where that takes more than the budget, `SearchMin` again with the tower count in the state (exact under the cap; FG
  over three narrow humps otherwise took 4 -> 6 towers, LP-30). Phase (c) must not undo the rule: when the search's members were clear
  and its reset raised a tower next to a station, that tower goes back to the search's member (the whole assignment when that breaks
  the line). The tram has
  TowerWorthM 0 (fewest towers first, EndRiseM breaks ties). `Fit` turns `StationRule` off for its free plan: the count the line needs is
  the fewest towers. A strict station-first order was tried and dropped: it stood FG8s 15-20 m from stations and inflated the fit's count.
- **Edge slope (SteepSlope).** The engine refuses a Temp edge whose node-to-node |dy| / xz length reaches the line prefab's
  m_MaxSlopeSteepness + 0.0005, or half of it 2x that (ValidationHelpers.cs:1435-1490, test :1462; the FlattenTerrain branch at :1435
  does not apply to lift lines). Line prefabs: SkiChairliftHV 0.8, SkiDetachableHV 0.9, SkiGondolaHV 1.2, SkiTramHV 2.5
  (`MaxEdgeSlope`, the defaults: the tool plans with the line prefab's live value, `With(maxEdgeSlope:)` at the pick). With `CheckEdgeSlope` (the lift tool only) every MinTowers transition, the breakover move and phase (c)'s raise keep
  |dy| <= 0.97 x MaxEdgeSlope x d between the nodes as built (towers at ground + E(member), the ends at `EndNodeYA` / `EndNodeYB`);
  `TotalViolation` counts the excess; `Build` sets `LiftPlanSpan.EdgeSlope` / `SlopeViolated`. Off for the fit: permanent edges are not
  validated. The legacy Anchors / Split / Infill path has no slope gate. A one-span segment clear for the floor runs the search when its
  edge fails the slope rule; where no tower arrangement exists (a chord between fixed end heights steeper than the rule: towers average
  its slope and cannot cut it) it stays one span and `Build` flags it, red in the preview.
- **Outputs.** `LiftTowerPlan.SampleFloorM` (MarginAt uses it), `StationA` / `StationB`, `EndCarrierA` / `EndCarrierB` (carrier at the end
  above the high side: negative = a sunk station; the tool warns SUNK STATION), `MaxEdgeSlope`.

## Fixed node count (Fit)
The engine's node count is fixed (equal split, CourseSplitSystem.cs:3485-3493), so `LiftTowerFitSystem` asks `Fit`:
1. `PlanFixed` = phase (c) only at the line's own nodes (each node gets its own profile sample);
2. else, on a straight line, the free plan with spans capped at the engine's max edge length, spare nodes at the longest spans' midpoints;
3. else Short (the better of the two, plus the tower count the line would need).

The fit never adds a tower: it forces `Pairs` off for its free plan (Needed unchanged, LiftBatch HD-5). It logs the train kinds per
tower with hysteresis (`LiftTowerFitSystem` keeps the last kind per node entity in memory only, cleared on load) as a
"Towers [angles]" line when a kind appears or changes; phase B1 changes no tower head.

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
