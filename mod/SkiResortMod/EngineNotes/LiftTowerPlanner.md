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
  what is left. Flags: Shoulder (the ground pokes through the chord), Valley (max height), Breakover. In `PlanCore` this legacy path
  only runs after the exact search failed three times (as is, removed spots allowed, the station clearance allowed); where it does not
  leave the segment clear either, the soft search replaces it (see "No clear plan" below).
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

### Heads: twins as members (B2, breakover-holddown-plan-2026-11-03 section 3)
A tower head variant is a twin MEMBER of the same placeholder with its own unique top and the support member's lanes (art v2:
docs/research/art/breakover-twins-2026-11/v2). `LiftTowerPlanConstants.Twins` (16: FG8 / FG12 Hold, Combo, Break; FG16 / FG20 Break;
DQ10 / DQ12 and GD12 / GD16 Hold, Combo) holds name, member, head and top; `TwinFilter` (set per load by `LiftTowerFitSystem`) drops
the ones the game has not loaded, so a build without the twin prefabs plans support heads only.
- Caps: `CapDeg(kind)` is the cap the planner judges with; FG support is the Break train's 32 (8 wheels) when every member has a Break
  twin, else the line cap 16 (`LineCapDeg`). So an FG crest of 16-32 deg takes a Break head instead of a pair; the FG pair cases of B1
  run without twins (LiftBatch BO-1..BO-6 `WithoutTwins`, BO-9 shows the Break head). Node `CapDeg` = the cap of the head it carries
  (`CapDeg(kind, head)`).
- Choice (`AssignHeads`, in `Build` after `SetAngles`, every plan): HoldDown -> Hold, PushPull -> Combo, Support -> Break past the line
  cap + 0.01 (kept down to the line cap - KindHystDeg when the tower had it), else Line (`WantHead`). The kind uses the previous kind
  as hysteresis in fixed-node plans (`PreviousKinds` / `PreviousHeads`, index = inner node; Plan and Fit's Moved ignore them; an
  unknown kind is the one the head implies, `LiftTowerEdit.KindOf`).
- A member without the twin, or whose twin's E would make an edge the engine refuses (CheckEdgeSlope, MaxEdgeSlope + 0.0005): the
  nearest member (fewest ladder steps, then the shorter) with that twin whose node, both spans and both edges stay clear, whose kind
  still wants that head and whose neighbor before it keeps its head; else the Line head and an angle note with the refusals per reason
  (LiftBatch HD-3). It never adds or moves a tower, so `Fit.Needed` is unchanged (HD-5).
- `HeadsAsGiven` evaluates the given heads (the fit's "before" of a line as it stands).
- The rope at a twin is `RopeAboveBase(member)`: its lanes are the member's (manifest rope_on_crown lane_y_new = member_lane_y; the
  LADDER gate compares the prefabs' anchors). The span and slope checks use the node y of the head (`NodeY(k, node)`).
- Rope above the NODE (`LiftTowerEdit.RopeAboveNodeM`, 2026-11-04, DECISIONS 592 u). A lane anchor is object-local (`LaneSystem.FindAnchors`:
  `ObjectUtils.LocalToWorld(transform, sub-lane point)`, LaneSystem.cs:2371-2416), so the rope stands at the tower pivot + the member's lane y,
  never at node y + a fixed offset: rope = ground + RopeAboveBase(member) = node y + RopeAboveBase(member) - E(member, head). A Line head is
  RopeAboveBase(0) for every member (E(m) = Top(m) - Top(0)); a twin is lower by Top(twin) - Top(member) (0.15 to 0.57 m in the tables)
  because its node is raised by its own, taller top. The lane-less end rope (lift tool `EndRope`, the fit's `ropeA` /
  `ropeB` fallback, the diag plan) reads the node's Elevation, MemberOf gives (member, head) and this helper gives the offset
  (`LiftTowerDiagSystem.RopeAboveNode` is the one entity-side reader). LiftBatch HD-7 checks it against the engine winner's anchor at all 29 heads.

### No clear plan: the soft search (planner-fallback-2026-11-04)
Field case (B2 bridge run): a plan-only FG line with ground ends planned 16 towers, 13 of them 10 m apart, VIOLATED, where B1 had
planned 5 clear. Cause: ground inside a ground end's tower-free zone (`NearStation` uses StationClearM at BOTH ends, also at a ground or
loose end) that the end's fixed rope cannot clear. Every exact search fails, `Anchors` fails, and the greedy `Split` scattered towers
along the whole segment. Not caused by the twins: B1's code gives 17 towers on the same ground (LiftBatch FB-1). The game's terrain
samples come from the height cascade and differ a little between sessions, so the same line planned clean in B1's run.
- **Rule.** After `LegacySegment`, `SegmentClear` checks the result (every span's floor and max height with the sag, every tower node,
  and with CheckEdgeSlope the engine's own edge limit MaxEdgeSlope + 0.0005). Clear: kept (LP-27's knoll keeps its legacy plan).
  Not clear: `SoftInsteadOfLegacy` takes the legacy nodes out and runs `MinTowers` once more with `m_Soft` (removed spots allowed, the
  station clearance kept): a span or a node short of the floor is allowed and its shortfall (m) is added to `MinCost.Viol`, which ranks
  before every other cost (additive, so the search stays exact). Max rope height, the edge slope rule, MaxSpanM, MinGap and the
  station clearance stay hard. Result: the least summed shortfall, then the fewest towers, as usual. Nothing found: the legacy nodes go back.
  `Fit`'s free plan goes through `Plan`, so its count on such a line follows (no scatter count); `LegacySpacing` keeps the old path.
- **Cost.** `SearchMin<TMode>` takes the mode as a struct type argument, so the strict search the JIT compiles has no soft branches.
  Warm, full-opt (.NET 8, tiered compilation off): DQ 1.2 km 17.0 vs 16.9 ms, DQ 2 km 28.0-28.5 vs 27.6-27.8 ms, the same as before.
  With .NET's default tiered PGO, LiftBatch's single-run numbers swing about 10 % either way (not a cost of the change). A soft
  segment costs more than the legacy split: on 877 random noisy lines that reach it, 9.8 ms on average (max 76) vs 3.9 ms. Lines with
  a clear plan never run it and plan bit-identically.
- **Order (FIX-LIST 49, `FallbackSegment`).** `Anchors` first; anchors found: the infill, `SegmentClear`, and the soft search only
  when that is not clear (as above). No anchors: the soft search FIRST and the greedy `Split` + infill only where it finds nothing.
  The split was computed and thrown away whenever the soft search answered; a split plus infill was never clear where `Anchors`
  found none (0 of 66 segments in LiftBatch and AutoGenBatch; the one clear legacy result, LP-27's knoll, comes from `Anchors`), and
  every LiftBatch plan is unchanged. Cost of a VIOLATED steep DQ plan (EI-49, the Eishain 760 m site stacked, a bump the end may not
  clear): 2,280 m 140 -> 115-118 ms, 1,520 m 81-87 -> 73-77 ms, 760 m unchanged (36-45 ms, noise). Where the rest goes (2,280 m): strict
  search 12 ms, the station-clearance retry 12 ms, `Anchors` 41 ms (fails), soft fewest 25 ms, soft weighted 25 ms. A clear 2,280 m
  DQ plan is 23-25 ms. In game about 5-10x (the 760 m site: 40.9 ms logged vs 4.3-8.1 ms here): ~50 ms per click on a 2 km DQ line
  needs the exact search itself 4-5x faster (per-segment interval tables or a hull over the span constraints; not done).
- **Quality** (the same 877 lines, soft vs the legacy plan): the legacy plan had an edge the engine refuses (SteepSlope, no slope gate)
  on 491; the soft plan fixes all 491 and adds none. Of the other 386, the soft plan has more summed span shortfall (> 0.05 m) on 11,
  each with about a third of the towers (7-8 vs 20-26: the legacy scatter bought 0.2-1.1 m). The field line with a 6 m bump 5 m from
  end B: 6 towers, only the end span short (-0.39 m, unavoidable from B's fixed rope), with and without twins (FB-1).
- **Gate (FB-3).** Twins vs `WithoutTwins` on 408 lines (the DR-1 shapes, the 192-line direction sweep with stations or ground ends,
  FG / DQ / GD crests and dips, the field line with bumps): never VIOLATED with twins where the line without is clear, and the tower
  count rises at most by the extra pairs the Break cap 32 makes. FB-2: with Pairs off, the twins change only heads (a twin head may
  take the nearest member with that twin, HD-3).
- **JIT note.** An in-game planner cost line that includes the first exercise of a code path (the first `Repair` of a session) includes
  its JIT: the B2 FG dip's "angles 3.58 ms" was cold (warm 0.19-0.33 ms).

### Breakover rule
Fixed-grip lines put a tower just before a breakover (a convex change of slope) so the rope follows the profile. So a Shoulder on convex
ground becomes a Breakover: it moves one sample past the crest toward the higher ground (uphill of the crest, not onto it) with the shortest
member that keeps both spans clear and adds no infill tower. `MinTowers` prefers the most towers on the uphill sample of a convex crest
(`Breaks`), never at the cost of an extra tower.

### Pitch cap and mast lean (DECISIONS 600)
- **Pitch** = the rope's slope through one tower, `LiftAlignMath.ChordPitchDeg`: the mean of the angles of its two chords (plumb rope
  point to plumb rope point, no sag). Per tower, never per line.
- **Hard cap** `LiftTowerPlanConstants.PitchCapDeg` (FG 38, DQ 40, GD 40, tram 55; research steep-angles-real-lifts section 5.1).
  `Over()` in the angle pass is max(bend past its cap, pitch past the pitch cap), so the pass repairs a steep tower like a bent one:
  (a) a shift within AngleShiftM with any member, (b) a pair on the shoulder (the tram too: only its pitch cap brings it to the pass,
  and it has no partner / pull candidates), (c) dropping the tower when the span over its spot stays fine (a longer span). The pass
  now runs whenever Pairs is on and the cap is finite (the tram included). Still over: `LiftTowerPlan.IsTooSteep`, `TooSteepTowers`,
  the Verdict / OverCapNote "N tower(s) TOO STEEP (pitch past P deg)", not Sound, the lift tool's PLAN_PITCH / TOWER_PITCH and a red
  tower (FIX-LIST 55's mechanism).
- **Soft caps** `PitchSoftDeg` (36 / 38 / 39 / 45) are stored and logged only: no search cost uses them.
- **Lean**: `SetLeans` (Build) gives every judged tower `PitchDeg`, `LeanDeg` (LiftAlignMath.LeanDeg) and the leaned anchor
  `AnchorAlongM` / `AnchorRopeM` (LiftAlignMath.LeanedAnchor, foot pivot). Pitch is judged on the plumb rope points, so the lean
  never feeds back into itself. The spans hang from the anchors (Build, `RopeAt`, the overlay): the floor is judged on the leaned
  rope, samples between a tower's foot and its anchor are skipped (the tower overhangs there); the max rope height stays on the plumb
  chord, as the search judged it (a lean only shifts a steep chord a few meters downhill, LP-5's wall).
- **LeanPass** (after the angle pass, and in PlanFixed when it assigns): where the leaned floor is short and the plumb one was not, the
  leaned end tower (else the other end) steps one member taller at a time while that lowers the summed leaned + plumb violation of
  its spans. Raise-only: a lower step broke LP-5 idempotence (the search is lean-unaware).
- Mirrored plans (from B) negate `PitchDeg` and mirror `AnchorAlongM`. The plan dump prints "pitch +P, lean L (anchor ... m along,
  ... m)" per tower (`PitchText`).
- Gates: LiftBatch PC-0 (bite measurement), PC-1..PC-6 (PitchCapTests.cs); docs research/pitch-cap-bite-2026-11-04.md.

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
  the caller marks as stations get it (`StationA` / `StationB`: the tool from its end kinds, a ground end included since FIX-LIST 48;
  the fit by the census rule `LiftCensusSystem.IsStationNode` since FIX-LIST 52; every line edge has HV rope lanes at both nodes, so a
  lane says nothing about a station); a loose end is a tower node with the full floor. The tower-free zone (`NearStation`, StationClearM) still applies at a loose or ground end;
  only the NoClearance retry lifts it.
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
- **Verdict (FIX-LIST 55).** `Clear` is spans only (floor, max height, edge slope); `Sound` adds "no tower over its cap"
  (`IsOverCap(i)`, `OverCapTowers`). Every plan-level text uses `Verdict` ("clear" only when sound, "spans clear, N tower(s) OVER CAP",
  "VIOLATED") or appends `OverCapNote` to "every span clear" (the fit and diag summaries), so no log line calls an over-cap plan clear.

## Station rope anchors (FIX-LIST 48)
A station's rope sits at its own HV rope sub-lane anchor, not at a tower's rope. Prefab-local y of the lowest anchor
(`StationDef.RopeAnchorLocalY`, the stub tip at y 0), against the shortest tower's rope (`RopeAboveBase(0)`): FG base 5.84 / top 5.24
(tower 7.27), DQ 3.70 (8.26), gondola 4.85 (10.28), tram 16.00 (26.25; the track rope, UtilityType HV; the haul rope is LV). Every
station anchor is LOWER, by 1.4 to 10.3 m. `StationEndRope(station, nodeY, frontGroundY)` = the lowest of the tower rope, the level
anchor and the anchor over the lot front (`StationDef.FrontLocalZ` = lot depth x 4, where vanilla stands the building): the lift tool's
ground ends (LiftLineEdit.md) and the auto planner's station ends (`SkiRunAutoPlanner.TowerPlan`, level only) plan with it. A slope
still moves a placed station: the Eishain FG base stood 5.1 m below its level anchor, the DQ base 26.3 m above it. Gate: LiftBatch EI-48
compares every catalog row with the mirror's station prefab (anchor, lot depth, stub tip) and checks the anchor is below the tower rope.

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

Never worse over a built line (FIX-LIST 52): `ToWrite(asBuilt, fit, built)` is the plan the fit writes. Over a built line an unsound plan
(`LiftTowerPlan.Sound`: every span clear and no tower over its cap) is written only when `LiftTowerPlan.WorseThan(plan, asBuilt)` finds it
no worse on every axis: min floor margin (`WorseMarginTolM` 0.05 m), `ViolatedSpans`, towers over cap, `MaxCapExcessDeg`. Null = keep the
line as built (LiftTowerFitSystem.md). Test: LiftBatch EI-52.

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
- Over a BUILT line (FIX-LIST 57) the "above" side is gone: the fit judges a built tower at its foot (`BuiltFootSampler`, max(ground,
  node y - E); `LiftTowerPlanner.FootAlong` keeps a tram float from counting twice) and `HeightWrite(old, target, built)` writes only a
  node below its target. The load re-fit had re-grounded a tram T60 1.05 m lower once our cut sank the ground under it
  (LiftTowerFitSystem.md "A built tower's ground is its foot"; LiftBatch RL-57, RL-1).
- Twins (B2): `ElevationM(c, member, head)` = Top(twin) - TopM[0], so the twin scores 1 (spare 0) and every other member less (tops
  >= 0.1 m apart, check-lift-align LADDER); `EngineWinner(c, e, out head, out score)` scores the ladder and the twins and
  `EnginePivotAboveGround(c, member, head, e)` = 0 for the planned head (LiftBatch HD-4). `MemberOf(c, e, out head)` reads a node back:
  the ladder member or twin within 0.05 m, else the tallest ladder member not above E with the Line head. Every twin E is within the
  line's m_ElevationRange.max (FG 12.5 <= 16, DQ 2.5178 <= 10, GD 4.568 <= 28); beyond it a later vanilla edit is unverified.

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

### Tram saddle cap and pedestals (DECISIONS 600 a)
- `SaddleHalfCapDeg` (tram `TramBendHalfCapDeg` 20, +inf otherwise; `With(saddleHalfCapDeg:)`). `LineCapDeg` / `CapDeg` without trains =
  2 x it, so the angle pass, `IsOverCap`, `OverCapTowers`, Sound and the red tower treat a tram saddle like a chair train. Keys: PLAN_SADDLE /
  TOWER_SADDLE (not TRAIN: a saddle has no wheels). PC-7 table (TS-1, 132 lines, towers 253 with no cap): cap 20: 25 lines take a
  breakover pair (+25 towers), 30 still warn; 17.5: 14 / 48; 15: 9 / 59. A tighter cap binds more lines a pair cannot fix. FB-3 trams never
  bind (widest half 14.1); steep set: 1 pair at 20, none warn.
- `SetFloats` (Build, after SetLeans; then SetLeans again): every leaned tram tower gets `FloatM` = `LiftAlignMath.TramFloatM` on the center
  profile (`CentreAt`, downhill = toward the lower neighbor); RopeM and the anchor foot rise with it; `NodeY` adds it. The search, the angle
  pass and the bend (SetAngles) stay on the unfloated ropes. Writers: LiftLineEdit.Emit and LiftTowerFitSystem put the node at ground + E +
  FloatM (HeightWrite's target includes it). In the sweep sets no tower needs it (the floor is 0 wherever the ground falls with the lean);
  it fires on a level spot under a 50+ deg rope, e.g. PC-8's bench: T60, lean 25, float 1.34 m.
- Leaned spans may reach samples past their nodes (a 25 deg FG20 moves its anchor about 8 m): Build and LeanedSpanViolation judge the floor
  over the anchors' range, not only between the nodes (PC-5).

### Carrier clearance (DECISIONS 600 c)
- `LiftPlanNode.HalfDeg` (AnglesAt: `LiftAlignMath.WorldHalfDeg` on the drawn rope, plumb rope points). `LiftTowerPlanConstants.WorldHalfMaxDeg(head)`
  (cached per head from `LiftAlignMath.WorldHalfTable`; `WorldHalfOn`, `With(worldHalfOn:)` turns it off for the before / after sweeps).
- The angle pass (`Over`) adds `half - WorldHalfMaxDeg(kind)` to the bend and pitch terms: it plans members, not heads, so a kind stands for
  the head it takes (HoldDown -> Hold, PushPull -> Combo, Support -> min(Line, Break)); the load cases run only when the bend or the half
  could bind (`MinWorldHalfMaxDeg`). A tower past it gets the usual repairs (shift / member, breakover pair, longer span); a pair whose new
  tower is past it is refused like any over-cap candidate.
- Build judges the PLACED head: `IsOverClearance(i)`, `ClearanceTowers`; not Sound; Verdict / OverCapNote "PAST CLEARANCE"; WorseThan axis;
  AngleText "half H PAST CLEARANCE L". Keys: the plan line PLAN_CLEARANCE (after PLAN_PITCH), the tower TOWER_CLEARANCE (after TOWER_PITCH;
  DEG = HalfDeg, LIMIT = the limit, one decimal); the red tower in the overlay.
- Bite (PC-9, all sets, the rule off vs on): 3 of 450 crossable chair lines newly warn (0.67 %), all crest-end FG g 0.30 back 20: the
  base-station exit (station rope 1.4 m under the ground, the ground at 0.3) needs a first tower whose arriving rope is 46-51 deg. The pitch
  cap used to fix it with a pair at 15 / 35 m whose first tower carried the 50.7 deg half; that pair is now refused and the single tower
  stays OVER CAP and PAST CLEARANCE (these plans end with one tower fewer than the uncapped plan, so PC-3 now checks "no added tower", not "the same count"). FB-3: no tower past it
  (steepest halves FG 40.5 / DQ 36.7 / GD 36.5 deg).
