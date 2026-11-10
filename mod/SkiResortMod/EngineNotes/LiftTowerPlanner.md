# LiftTowerPlanner: the planner's ground model and phases, the engine facts behind a fitted node, and the tram's 3 km span

`LiftTowerPlanner` is the pure tower planner for a lift line. It uses no ECS and no Game types. LiftBatch tests it headless
(`LiftPlannerTests`). It runs once per edit (or once per printed diag block), so it is plain C#, not Burst. Each planner instance
reuses its arrays. The planner is deterministic:

- No randomness.
- No hash order.
- Ties break by the sample index.
- The plan is the same whichever end the caller passes as A (Direction, below).

Citations are the decompile baseline 1.6.0f1.

## Input
- A terrain sampler (along-line meters from station A, lateral meters, + = right of A to B). It samples every StepM along the line at the
  center and at +-HalfWidthM (the engine's own side samples, CourseSplitSystem.cs:3566-3573, half the net width).
- Optionally a second sampler from B (`terrainFromB`: meters from B, + = right of B to A) for the plans that work from B. Without it, the
  planner mirrors the first (`terrain(L - s, -l)`, equal within float rounding).
- The line length (xz) and the two station anchors' rope heights (world y of the haul rope at the stub).
- `LiftTowerPlanConstants` for the lift type (ladder members, rope offset, carrier drop, floor, max rope height, spans, width, sag).
- Optionally seed nodes (the output of an earlier plan). The planner keeps them as anchors, so re-planning a plan is a fixed point.

Constants provenance:

- Ladder tops are the tower manifests' top_anchor_y = ObjectGeometryData m_Size.y, confirmed in game (member lists in the mod log).
- Rope offset = shortest top - catalog AxleM (lane y on the shortest tower). This equals every member's top - axle in the manifests
  (FG 1.974, DQ 1.95, GD 2.0, tram stubs 3.75).
- Drop = catalog HangM.
- Half width = piece m_Width / 2 (SkiChairliftPiece / SkiDetachablePiece 6, SkiGondolaPiece 8.2, SkiTramPiece 15).
- Sag = the rope lane's m_Hanging = the catalog's Envelope.LaneHanging (FG SkiFixedGripHaulRope 0.02, DQ / GD 0.025, tram 0.04 since the
  rope sag round). Before that round, FG was 0.01 and the others 0.004.
- Drop = HangM stays conservative: carriers ride the rope point since F5, GripTopAboveRopeM (0.08 / 0.22 m) higher than HangM below the rope.

## Ground model (per sample)
- center = the ground under the line axis (the tower base: our own spawn stands on it).
- high = max(center, left, right): the clearance ground. The rope must keep the chair floor over the higher side. This is the engine's
  "lower elevation side" (cmin of Net.Elevation, SubObjectSystem.cs:980), so the side that governs the engine's ladder also governs ours.
  The planner flags a node SideHill when its high and low side differ by more than SideHillM.
- rope(t) on a span = the straight chord between the two rope points minus the engine's sag. LaneSystem.cs:4128-4134 lowers the bezier's
  b / c by d*m_Hanging*4/3. The result is a parabola 4*d*m_Hanging*t(1-t) (mid-span sag = d*m_Hanging).
- floor margin(t) = rope(t) - CarrierDropM - high(t) - floor(t) (>= 0 required). floor(t) = FloorM, ramped near a station end (Station
  ends below).
- rope height(t) = rope(t) - center(t) (<= MaxRopeAboveGroundM when that is > 0, the tram has no cap).

## Phases
- (a) Anchors: the planner splits the station-to-station chord (or a seed-to-seed span) that has a violation where the terrain forces it.
  - It solves the split exactly (`Anchors`: a shortest-path search over the samples x ladder members). The old method was a greedy
    recursive worst-intrusion split, which put far too many towers on a 40 m wall.
  - Ranking: fewest towers (anchors + the infill (b) will add), then fewest anchors, shortest members, anchors nearest the worst-intrusion
    sample.
  - The floor test ignores the sag on spans that (b) will split (their sub-spans sag far less). It keeps the sag on spans that (b) leaves
    whole (FloorSag).
  - Anchor spans <= 3 x MaxSpanM.
  - If no clear arrangement exists, the greedy split (`Split`) minimizes what is left.
  - Flags: Shoulder (the ground pokes through the chord), Valley (max height), Breakover.
  - In `PlanCore`, this legacy path runs only after the exact search failed three times (as is, removed spots allowed, the station
    clearance allowed). Where it does not leave the segment clear either, the soft search replaces it (see "No clear plan" below).
- (b) Infill: a span between anchors gets max(ceil(len / MaxSpanM), round(len / TypicalSpanM)) sub-spans. The planner tries the
  equal-spacing samples first. Else each infill node slides inside +-half a sub-span of its spot to the clear spot with the shortest
  member, nearest the spot. No sub-span exceeds MaxSpanM or 1.5 x TypicalSpanM - StepM (so a re-plan rounds it to one span: idempotence).
  If no arrangement is clear, the planner tries one to three extra sub-spans. It keeps the best and reports its violation.
- (c) Elevation: every tower starts at the shortest member that keeps the floor at its own node (side-hill). Then, while a span has a
  floor violation, the end nearer the worst sample goes up one member. (If that end is at the top of the ladder or is a station, the other
  end goes up.) The planner never uses members whose rope is above MaxRopeAboveGroundM. If (c) leaves a violation that the (a) / (b)
  members did not have, the planner keeps the (a) / (b) members.
- (d) Rope angles (breakover / hold-down pairs, tool plans only: `Pairs`, set by `LiftLineEdit.Replan`): a local post-pass after (c).
  - The planner repairs first the tower whose rope deflection (`LiftAlignMath.DeflectionDeg`, sag h) is furthest past its train's cap. It
    re-evaluates after every change and makes one repair per original tower.
  - Candidates, cheapest first (fewer towers, fewer ladder steps, smaller max |deflection|, shorter spacing, nearer the old spot):
    - (1) The tower shifts within AngleShiftM 20 m with any member. Or, when it stands PairMaxM or nearer beside a fixed tower (a pin, a seed,
      the junction end), it moves anywhere in that tower's partner window. A fixed tower only changes member.
    - (2) A free tower is replaced by a pair PairMinM..PairMaxM (10..40 m) apart within 20 m of its spot.
    - (3) A fixed tower keeps its spot and gets a new free partner.
    - (4) Two fixed towers 40 m or nearer are the player's pair. The planner only re-members them.
    - (5) A fixed tower keeps its spot and pulls its free neighbor in as its partner.
  - Every candidate must meet all of these conditions:
    - Its spans stay clear (sag, edge slope, MaxSpanM).
    - It keeps the station clearance and the removed spots.
    - Both touched towers are within their caps.
    - The untouched neighbors are no further past their caps.
    - It never leaves three towers in a row 40 m or nearer apart (MaxGroup 2).
  - If no candidate is feasible, the tower stays, flagged OverCap, and the note names the refusals per reason.
  - The planner judges a line end that joins a built line against that line's far node rope point (`EndOuterDistA` / `EndOuterRopeA`,
    B likewise, from `LiftLineToolSystem.OuterPoint`).
  - The angle pass judges deterministic work (`AngleWork`, rope-angle evaluations: LiftBatch CO-1 bounds it). It reports its wall time
    (`AnglesMs`): 0.001 ms on an ordinary 1.2 km DQ, 0.17 ms on a 2 km DQ with three sharp crests.
  - Practical reach found by LiftBatch (BO-1, BO-2):
    - DQ pairs a 28 deg ground chord change (0.60 -> 0.05).
    - GD pairs 36 deg (0.90 -> 0.10).
    - FG pairs only about 8.5 deg (its 4 m ladder steps and 90 m max span). The plan estimated 13.9.

    So sharper FG crests stay single and flagged.
- (e) Output: ordered nodes (along, ground, high side, member, rope y, flags) plus the per-span clearance profile. On every judged node
  (every plan, the fit's included), the output also has the deflection, the two load cases, the train kind and the cap (`SetAngles`).
  `AppendLog` writes a "Towers [plan]" block in the "Towers [nodes]" format of `LiftTowerDiagSystem`, so the two compare line by line.
  Each tower has its angle text ("d +12.3 (tight +9.1 / heavy +17.0) S [pair]", "OVER CAP" when flagged).

### Heads: twins as members (B2, breakover-holddown-plan-2026-11-03 section 3)
A tower head variant is a twin MEMBER of the same placeholder, with its own unique top and the support member's lanes (art v2:
docs/research/art/breakover-twins-2026-11/v2). `LiftTowerPlanConstants.Twins` (16 twins: FG8 / FG12 have Hold, Combo and Break. FG16 / FG20 have Break.
DQ10 / DQ12 and GD12 / GD16 have Hold and Combo) holds name, member, head and top. `TwinFilter` (set per load by `LiftTowerFitSystem`) drops the
twins that the game did not load. So a build without the twin prefabs plans support heads only.
- Caps: `CapDeg(kind)` is the cap the planner judges with. FG support is the Break train's 32 (8 wheels) when every member has a Break
  twin, else the line cap 16 (`LineCapDeg`). So an FG crest of 16-32 deg takes a Break head instead of a pair. The FG pair cases of B1
  run without twins (LiftBatch BO-1..BO-6 `WithoutTwins`, BO-9 shows the Break head). Node `CapDeg` = the cap of the head it carries
  (`CapDeg(kind, head)`).
- Choice (`AssignHeads`, in `Build` after `SetAngles`, every plan):
  - HoldDown -> Hold.
  - PushPull -> Combo.
  - Support -> Break past the line cap + 0.01 (kept down to the line cap - KindHystDeg when the tower had it), else Line (`WantHead`).

  The kind uses the previous kind as hysteresis in fixed-node plans (`PreviousKinds` / `PreviousHeads`, index = inner node). Plan and
  Fit's Moved ignore them. An unknown kind is the one the head implies (`LiftTowerEdit.KindOf`).
- A member has no twin, or its twin's E would make an edge the engine refuses (CheckEdgeSlope, MaxEdgeSlope + 0.0005). In that case the
  planner takes the nearest member (fewest ladder steps, then the shorter) that meets all of these conditions:
  - It has that twin.
  - Its node, both spans and both edges stay clear.
  - Its kind still wants that head.
  - The neighbor before it keeps its head.

  If no member meets them, the planner uses the Line head and an angle note with the refusals per reason (LiftBatch HD-3). It never adds
  or moves a tower, so `Fit.Needed` is unchanged (HD-5).
- `HeadsAsGiven` evaluates the given heads (the fit's "before" of a line as it stands).
- The rope at a twin is `RopeAboveBase(member)`: its lanes are the member's (manifest rope_on_crown lane_y_new = member_lane_y, and the
  LADDER gate compares the prefabs' anchors). The span and slope checks use the node y of the head (`NodeY(k, node)`).
- Rope above the NODE (`LiftTowerEdit.RopeAboveNodeM`, 2026-11-04, DECISIONS 592 u). A lane anchor is object-local (`LaneSystem.FindAnchors`:
  `ObjectUtils.LocalToWorld(transform, sub-lane point)`, LaneSystem.cs:2371-2416). So the rope stands at the tower pivot + the member's lane y, never at node y + a fixed offset. Formula: rope = ground + RopeAboveBase(member) = node y + RopeAboveBase(member) - E(member, head).
  - A Line head is RopeAboveBase(0) for every member (E(m) = Top(m) - Top(0)).
  - A twin is lower by Top(twin) - Top(member) (0.15 to 0.57 m in the tables), because its node is raised by its own, taller top.

  The lane-less end rope (lift tool `EndRope`, the fit's `ropeA` / `ropeB` fallback, the diag plan) reads the node's Elevation. MemberOf
  gives (member, head), and this helper gives the offset (`LiftTowerDiagSystem.RopeAboveNode` is the one entity-side reader). LiftBatch
  HD-7 checks it against the engine winner's anchor at all 29 heads.

### No clear plan: the soft search (planner-fallback-2026-11-04)
Field case (B2 bridge run): a plan-only FG line with ground ends planned 16 towers, 13 of them 10 m apart, VIOLATED. B1 had planned 5
clear. Cause: ground inside a ground end's tower-free zone (`NearStation` uses StationClearM at BOTH ends, also at a ground or loose end)
that the end's fixed rope cannot clear. Every exact search fails, and `Anchors` fails. Then the greedy `Split` scattered towers along the
whole segment. The twins did not cause this: B1's code gives 17 towers on the same ground (LiftBatch FB-1). The game's terrain samples come
from the height cascade and differ a little between sessions, so the same line planned clean in B1's run.
- **Rule.** After `LegacySegment`, `SegmentClear` checks the result (every span's floor and max height with the sag, every tower node,
  and with CheckEdgeSlope the engine's own edge limit MaxEdgeSlope + 0.0005).
  - Clear: the planner keeps the result (LP-27's knoll keeps its legacy plan).
  - Not clear: `SoftInsteadOfLegacy` takes the legacy nodes out and runs `MinTowers` once more with `m_Soft` (removed spots allowed, the
    station clearance kept). A span or a node short of the floor is allowed. Its shortfall (m) is added to `MinCost.Viol`, which ranks
    before every other cost (additive, so the search stays exact).
  - Max rope height, the edge slope rule, MaxSpanM, MinGap and the station clearance stay hard.
  - Result: the least summed shortfall, then the fewest towers, as usual.
  - Nothing found: the legacy nodes go back.
  - `Fit`'s free plan goes through `Plan`, so its count on such a line follows (no scatter count). `LegacySpacing` keeps the old path.
- **Cost.** `SearchMin<TMode>` takes the mode as a struct type argument, so the strict search the JIT compiles has no soft branches.
  Warm, full-opt (.NET 8, tiered compilation off): DQ 1.2 km 17.0 vs 16.9 ms, DQ 2 km 28.0-28.5 vs 27.6-27.8 ms, the same as before.
  With .NET's default tiered PGO, LiftBatch's single-run numbers swing about 10 % either way (not a cost of the change). A soft segment costs more than the legacy split. On 877 random noisy lines that reach it, it takes 9.8 ms on average (max 76) vs 3.9 ms. Lines with
  a clear plan never run it and plan bit-identically.
- **Order (FIX-LIST 49, `FallbackSegment`).** `Anchors` runs first.
  - Anchors found: the infill, then `SegmentClear`, and the soft search only when that is not clear (as above).
  - No anchors: the soft search FIRST, and the greedy `Split` + infill only where it finds nothing.

  The split was computed and thrown away whenever the soft search answered. A split plus infill was never clear where `Anchors` found none
  (0 of 66 segments in LiftBatch and AutoGenBatch). The one clear legacy result, LP-27's knoll, comes from `Anchors`. Every LiftBatch plan
  is unchanged. Cost of a VIOLATED steep DQ plan (EI-49, the Eishain 760 m site stacked, a bump the end may not clear):
  - 2,280 m: 140 -> 115-118 ms.
  - 1,520 m: 81-87 -> 73-77 ms.
  - 760 m: unchanged (36-45 ms, noise).

  Where the rest goes (2,280 m):
  - Strict search: 12 ms.
  - The station-clearance retry: 12 ms.
  - `Anchors`: 41 ms (fails).
  - Soft fewest: 25 ms.
  - Soft weighted: 25 ms.

  A clear 2,280 m DQ plan is 23-25 ms. In game the cost is about 5-10x (the 760 m site: 40.9 ms logged vs 4.3-8.1 ms
  here). ~50 ms per click on a 2 km DQ line needs the exact search itself 4-5x faster (per-segment interval tables or a hull over the
  span constraints, not done).
- **Quality** (the same 877 lines, soft vs the legacy plan): the legacy plan had an edge the engine refuses (SteepSlope, no slope gate)
  on 491. The soft plan fixes all 491 and adds none. Of the other 386, the soft plan has more summed span shortfall (> 0.05 m) on 11.
  Each of those has about a third of the towers (7-8 vs 20-26). The legacy scatter bought 0.2-1.1 m. The field line with a 6 m bump 5 m from end B has 6 towers, with and without twins (FB-1). Only the end span is short (-0.39 m, unavoidable from B's fixed rope).
- **Gate (FB-3).** Twins vs `WithoutTwins` on 408 lines (the DR-1 shapes, the 192-line direction sweep with stations or ground ends,
  FG / DQ / GD crests and dips, the field line with bumps). A line is never VIOLATED with twins where the line without twins is clear. The
  tower count rises at most by the extra pairs the Break cap 32 makes. FB-2: with Pairs off, the twins change only heads (a twin head may
  take the nearest member with that twin, HD-3).
- **JIT note.** If an in-game planner cost line includes the first exercise of a code path (the first `Repair` of a session), it includes the JIT of that path. Example: the B2 FG dip's "angles 3.58 ms" was cold (warm 0.19-0.33 ms).

### Breakover rule
Fixed-grip lines put a tower just before a breakover (a convex change of slope) so the rope follows the profile. So a Shoulder on convex
ground becomes a Breakover. It moves one sample past the crest toward the higher ground (uphill of the crest, not onto it). It takes the shortest member that keeps both spans clear and adds no infill tower. `MinTowers` prefers the most towers on the uphill sample of a convex crest
(`Breaks`), never at the cost of an extra tower.

### Pitch cap and mast lean (DECISIONS 600)
- **Pitch** = the rope's slope through one tower, `LiftAlignMath.ChordPitchDeg`: the mean of the angles of its two chords (plumb rope
  point to plumb rope point, no sag). Per tower, never per line.
- **Hard cap** `LiftTowerPlanConstants.PitchCapDeg` (FG 38, DQ 40, GD 40, tram 55, research steep-angles-real-lifts section 5.1).
  `Over()` in the angle pass is max(bend past its cap, pitch past the pitch cap). So the pass repairs a steep tower like a bent one:
  - (a) a shift within AngleShiftM with any member.
  - (b) a pair on the shoulder (the tram too: only its pitch cap brings it to the pass, and it has no partner / pull candidates).
  - (c) dropping the tower when the span over its spot stays fine (a longer span).

  The pass now runs whenever Pairs is on and the cap is finite (the tram included). If a tower is still over, these follow:
  - `LiftTowerPlan.IsTooSteep`, `TooSteepTowers`.
  - The Verdict / OverCapNote "N tower(s) TOO STEEP (pitch past P deg)".
  - Not Sound.
  - The lift tool's PLAN_PITCH / TOWER_PITCH.
  - A red tower (FIX-LIST 55's mechanism).
- **Soft caps** `PitchSoftDeg` (36 / 38 / 39 / 45) are stored and logged only: no search cost uses them.
- **Lean**: `SetLeans` (Build) gives every judged tower `PitchDeg`, `LeanDeg` (LiftAlignMath.LeanDeg) and the leaned anchor
  `AnchorAlongM` / `AnchorRopeM` (LiftAlignMath.LeanedAnchor, foot pivot). Pitch is judged on the plumb rope points, so the lean
  never feeds back into itself. The spans hang from the anchors (Build, `RopeAt`, the overlay). The planner judges the floor on the leaned
  rope. It skips samples between a tower's foot and its anchor (the tower overhangs there). The max rope height stays on the plumb
  chord, as the search judged it (a lean only shifts a steep chord a few meters downhill, LP-5's wall).
- **LeanPass** (after the angle pass, and in PlanFixed when it assigns). It applies where the leaned floor is short and the plumb floor was not. The leaned end tower (else the other end) steps one member taller at a time, while that lowers the summed leaned + plumb violation of
  its spans. It only raises: a lower step broke LP-5 idempotence (the search is lean-unaware).
- Mirrored plans (from B) negate `PitchDeg` and mirror `AnchorAlongM`. The plan dump prints "pitch +P, lean L (anchor ... m along,
  ... m)" per tower (`PitchText`).
- Gates: LiftBatch PC-0 (bite measurement), PC-1..PC-6 (PitchCapTests.cs). Docs: research/pitch-cap-bite-2026-11-04.md.

## Direction: the same plan from either end (planner-direction-2026-11-04)
The phases work in one direction. Every tie and remainder resolves toward the end they start from:
- The sample grid starts there, and its short last step lands at the other end. `MinGap` counts samples, so the fallback can stand a tower StepM + remainder from the far end.
- `SearchMin` keeps the first of equal-cost states.
- The station rule's one extra tower goes to the first segment.
- The local passes (crest marks, phase (c), (d), `Fit`'s spare midpoints) prefer the lower index.

Before 2026-11-04, that end was the player's first click. So the same line planned differently from each end (91 of 192 synthetic lines).
Example: the FG station pair at x -1200 in the B1 bridge run had 6 towers with a pair from the base, and 7 without from the top. Report:
docs/research/planner-direction-2026-11-04.md.
- **Rule.** `Plan` works from the end with the lower rope (`PlansFromB`, an exact float compare). Equal ropes fall back to the ends'
  world xz (`EndKeyA` / `EndKeyB`: the smaller x, then z, the tool sets them). With no keys, it works from A.

  Working from B, it takes these steps:
  1. It mirrors the seeds and `Forbidden`.
  2. It swaps every per-end input (`StationA/B`, `EndNodeYA/B`, `EndOuterDistA/B`, `EndOuterRopeA/B`).
  3. It plans (`PlanCore`, the old body, untouched).
  4. It mirrors the plan back (`Mirrored`: nodes, spans, the profile, the per-end outputs).

  The code restores the inputs in a `finally`. A deflection reads the same from either side, so the angles are copied. The code writes the
  angle notes in the caller's frame (`NoteAt` / `NoteNode`, a PAIR note names its towers in the caller's order).
- **Exactness.** The tool's plan is bit-identical both ways: `LiftLineEdit` snaps pins to the working end's grid and passes a sampler
  from B (`PointFromB`, the same world points as the reversed edit's `PointAt`). Without a B sampler (the fit, the diag, direct
  callers), the planner mirrors the ground itself. The plans agree within the snap step with the same members and pairs
  (LiftBatch DR-1).
- **Scope.** `Fit`'s free plan goes through `Plan`, so it is canonical too. `PlanFixed` (the line's own nodes) and the kept path work
  from A as before. `AsGiven` (internal, tests only) replays the old behavior (LP-26's pre-pass field plan, the DR sweeps' binding
  checks).
- **Quality and cost.** The rule picks one of the two old plans. It adds no planning pass. On 825 synthetic lines planned both ways
  by the old planner, 714 differ. 702 of them have the same clearance, flags and tower count. 4 are better from the lower rope, and 8
  from the higher (FG / DQ only). A line whose plan from the lower rope leaves a crest flagged OverCap can lose a pair the other
  end found (LiftBatch DR-4: FG crest 402.3 m).

## Crossings with another lift type (DECISIONS 602, FIX-LIST 60 a)
- Input: `Crossings` (IList<LiftPlanCrossing>, the tool's CollectCrossings, along from A). Output: `LiftTowerPlan.Crossings` (same order,
  with Side, RequiredM, PlannedM, Refusal) and `CrossingsRefused`. Null / empty: `Plan` runs `PlanOnce` as before (bit-identical, CX-0).
- The box rule (LiftLineToolSystem.md, Type layers):
  - OVER needs the drawn curve's lowest footprint point (y - eN |slope|) at least the crossed curve's highest (yC + |gC| eC) + (crossed max - drawn min) + `CrossMarginM` 0.5.
  - UNDER is the mirror, minus `MaxTwinRiseM` (a twin head can lift a node after the search).
  - `CrossReach`: eN = (wC + wN cos)/(2 sin), eC = (wN + wC cos)/(2 sin), the strips' overlap.
- In the search:
  - The curve at the crossing is node chord (tower node = rope - RopeAboveBase(0), `NodeOff`, ends EndNodeYA / B) minus the sag
    4 d h t (1 - t). It is piecewise linear in the far node, so `CrossBounds` turns it into an interval on the far rope inside
    `SpanInterval`.
  - `SpanViolation` counts the shortfall (as a floor violation, so phase (c) raises the nearer end).
  - The soft search keeps it hard.
  - No tower in `CrossBandM` (both half widths + 2 m over sin). This is folded into `NearStation`, and NoClearance does not lift it.
- `Plan`:
  - Refusals known before planning drop out (`CrossRefusalBeforePlan`):
    - Under `CrossMinAngleDeg` 20 = Shallow.
    - Within StationClearM of a drawn station end or of a crossed station node = Station.
    - Closer to a crossed tower than eC + its head half width + 3 m = Tower.
  - All others go OVER. Then each, one at a time, goes UNDER where `UnderRoom` >= 0, and stays UNDER when `CrossBetter` (fewer refused,
    Clear, fewer towers, fewer ladder steps).
  - A crossing still short is refused (Clearance), and the line is replanned without it. So the preview is the line as it would stand (not
    the soft search's row of tallest towers). The same happens when the crossings are met only by a plan that is not Clear while the line
    without them is.
  - `BuildCrossings` judges the built nodes (heads, floats). A pin in the band = OwnTower.
- Reach (LiftBatch CX-1): the over side has a window between the crossed curve + ~15 m and the 30 m rope cap. So an over-crossing
  works where the crossed curve sits within a few meters of the ground (valley floors, sagging spans). A crossed curve 6+ m up on flat
  ground is refused. Flatter angles widen the footprints: at 35-60 deg a crossed slope of 0.15 often tips a case into a refusal.
- Unverified in game: the sag and footprint model (the CX oracle checks the planner against the same model, not against the engine).

## Station ends, the station-side rule and the edge slope (2026-10-26 lift pass)
Report: docs/research/lifts/lift-pass-2026-10-26.md. Tests: LiftBatch LP-26..LP-30 (LP-30: the review fixes,
docs/research/review-153b64e-2026-10-26.md F3 / F4 / F9 / F10).
- **Approach floor.** Carriers leave a station at loading height (DQ: bullwheel rope 3.70 m above the platform, hang 3.30 m = 0.40 m).
  So the floor at a station end ramps linearly. It starts at the carrier's own height at the end (0, or negative at a sunk station). It ends at FloorM at StationClearM (FG 12.2, DQ 25.6, GD 25.4, tram 40 m): `LiftTowerPlanConstants.ApproachFloor`. Before, FloorM applied from the first
  sample and forced a tall tower right at every station via the NoClearance fallback (field DQ: DQ12 @10 m and DQ18 @1,015 m).

  Only ends that the caller marks as stations get the ramp (`StationA` / `StationB`):
  - The tool sets them from its end kinds, a ground end included since FIX-LIST 48.
  - The fit sets them by the census rule `LiftCensusSystem.IsStationNode` since FIX-LIST 52. (Every line edge has HV rope lanes at both
    nodes, so a lane says nothing about a station.)

  A loose end is a tower node with the full floor. The tower-free zone (`NearStation`, StationClearM) still applies at a loose or ground
  end. Only the NoClearance retry lifts it. The retry keeps the station floor (next item).
- **Station floor (task R2-l, DECISIONS 637).** No planner tower stands closer than `StationFloorM` to a station end.
  StationFloorM is the smaller of 20 m (`StationTowerFloorM`) and StationClearM. So FG keeps 12.2 m, and DQ, GD and the tram get 20 m.
  `NearStation` applies the floor at a station end (StationA / StationB), also in the NoClearance retry. Before, the retry went down to MinGap (10 m).
  - The game rule. `CourseSplitSystem.CheckNodeGeometry` (1.6.0f1 CourseSplitSystem.cs:587-627, the test at :600) joins a new course to an
    existing node. It does this when the course passes within the course's half width plus the node's half width of that node.
    The stub tip node is a SkiLiftMarker (SkiLiftMarkerPiece m_Width 16, half width 8 m).
  - The span to a tower nearer than that ends on the stub node. It runs past the tower. The game then flags OverlapExisting
    (Game.Objects ValidationHelpers.cs, NetIterator.CheckOverlap3D, :1035-1182).
  - `StubSnapM` = HalfWidthM + 8 m: FG and DQ 11 m, GD 12.1 m, tram 15.5 m. The floor stands above the snap for every type.
  - Field cases: refused at 6, 7, 9 and 10 m. Built at 12 m and more. The refused edge midpoint is the mean of the previous node
    and the stub (r1e 1697 = (1575 + 1819) / 2).
  - `StationFloor` (on by default) turns the rule off for one test only (LiftBatch LP-34).
  - Effect on the LiftBatch corpus: 243 of 13,279 plans changed (DQ and GD lines with an end tower 10-19 m from a station).
    LP-22 (GD 1,408 m) keeps its plan.
- **The lift queue's rule (task R2-l).** `QueueRule` / `QueueBuild` say what the Resort Planner's queue does with a plan. A Sound plan
  builds. A plan with every span clear and exactly ONE tower over its sheave-train limit (`OverLimitTowers` = 1) builds flagged. Any other
  plan waits for the player. `OverLimitTowers` is OverCapTowers on a type with trains, and 0 on the tram. `QueueWaitKey` names the
  reason of a plan that waits. Two or more towers over the limit give PLAN_TRAINS. LiftBatch PS-13 and LP-34 pin it.
- **Station-side rule.** `MinCost.EndRiseM` = the rise above the shortest member of each tower next to a station end. With
  `TowerWorthM` > 0 (FG / DQ / GD 8 m), the search minimizes towers x TowerWorthM + EndRiseM first. One more tower buys 8 m of shorter
  station towers (ordinary 1,200 m DQ slope: DQ18 @105 / DQ20 @1,075 with 5 towers -> DQ10 at both ends with 6). The rule allows at most
  ONE tower over the fewest per line (`m_ExtraTowers`, DECISIONS 429). A segment touching a station runs the fewest-towers search first,
  then the weighted one. Where that takes more than the budget, it runs `SearchMin` again with the tower count in the state. This is exact under the cap. LP-30: FG over three narrow humps otherwise took 4 -> 6 towers. Phase (c) must not undo the rule. When the search's members
  were clear and its reset raised a tower next to a station, that tower goes back to the search's member. If that breaks the line, the whole assignment goes back. The tram has TowerWorthM 0 (fewest towers first, EndRiseM breaks ties). `Fit` turns `StationRule` off for its free
  plan: the count the line needs is the fewest towers. We tried a strict station-first order and dropped it. It stood FG8s 15-20 m from
  stations and inflated the fit's count.
- **Edge slope (SteepSlope).** The engine refuses a Temp edge whose node-to-node |dy| / xz length reaches the line prefab's
  m_MaxSlopeSteepness + 0.0005, or half of it 2x that (ValidationHelpers.cs:1435-1490, test :1462). The FlattenTerrain branch at :1435
  does not apply to lift lines. Line prefabs: SkiChairliftHV 0.8, SkiDetachableHV 0.9, SkiGondolaHV 1.2, SkiTramHV 2.5
  (`MaxEdgeSlope`, the defaults). The tool plans with the line prefab's live value, `With(maxEdgeSlope:)` at the pick. With `CheckEdgeSlope` (the lift tool only), three steps keep |dy| <= 0.97 x MaxEdgeSlope x d between the nodes as built: every MinTowers transition, the breakover move and phase (c)'s raise. The nodes as built are towers at ground + E(member) and the ends at `EndNodeYA` / `EndNodeYB`.
  `TotalViolation` counts the excess. `Build` sets `LiftPlanSpan.EdgeSlope` / `SlopeViolated`. Off for the fit: permanent edges are not
  validated. The legacy Anchors / Split / Infill path has no slope gate. A one-span segment clear for the floor runs the search when its
  edge fails the slope rule. Where no tower arrangement exists (a chord between fixed end heights steeper than the rule), it stays one span. Towers average its slope and cannot cut it. `Build` flags it, red in the preview.
- **Outputs.** `LiftTowerPlan.SampleFloorM` (MarginAt uses it), `StationA` / `StationB`, `EndCarrierA` / `EndCarrierB` (carrier at the end
  above the high side: negative = a sunk station, and the tool warns SUNK STATION), `MaxEdgeSlope`.
- **Verdict (FIX-LIST 55).** `Clear` is spans only (floor, max height, edge slope). `Sound` adds "no tower over its cap"
  (`IsOverCap(i)`, `OverCapTowers`). Every plan-level text uses one of two forms: `Verdict` ("clear" only when sound, "spans clear, N tower(s) OVER CAP", "VIOLATED"), or `OverCapNote` appended to "every span clear" (the fit and diag summaries). So no log line calls an over-cap plan clear.

## Station rope anchors (FIX-LIST 48)
A station's rope sits at its own HV rope sub-lane anchor, not at a tower's rope. The values are the prefab-local y of the lowest anchor
(`StationDef.RopeAnchorLocalY`, the stub tip at y 0). Each value is followed by the shortest tower's rope (`RopeAboveBase(0)`) in parentheses: FG base 5.84 / top 5.24
(tower 7.27), DQ 3.70 (8.26), gondola 4.85 (10.28), tram 16.00 (26.25, the track rope, UtilityType HV. The haul rope is LV). Every
station anchor is LOWER, by 1.4 to 10.3 m. `StationEndRope(station, nodeY, frontGroundY)` = the lowest of the tower rope, the level
anchor and the anchor over the lot front (`StationDef.FrontLocalZ` = lot depth x 4, where vanilla stands the building). The lift tool's
ground ends (LiftLineEdit.md) and the auto planner's station ends (`SkiRunAutoPlanner.TowerPlan`, level only) plan with it. A slope
still moves a placed station: the Eishain FG base stood 5.1 m below its level anchor, the DQ base 26.3 m above it. Gate: LiftBatch EI-48
compares every catalog row with the mirror's station prefab (anchor, lot depth, stub tip) and checks the anchor is below the tower rope.

## Fixed node count (Fit)
The engine's node count is fixed (equal split, CourseSplitSystem.cs:3485-3493), so `LiftTowerFitSystem` asks `Fit`:
1. `PlanFixed` = phase (c) only at the line's own nodes (each node gets its own profile sample).
2. Else, on a straight line, the free plan with spans capped at the engine's max edge length, spare nodes at the longest spans' midpoints.
3. Else Short (the better of the two, plus the tower count the line would need).

The fit never adds a tower: it forces `Pairs` off for its free plan (Needed unchanged, LiftBatch HD-5). It logs the train kinds per tower with hysteresis as a "Towers [angles]" line when a kind appears or changes. (`LiftTowerFitSystem` keeps the last kind per node entity in memory only, cleared on load.) Phase B1 changes no tower head.

Hysteresis: the members the line already carries win when they clear it as they stand. Phase (c) re-run on ground that moved a few
decimeters (our own carve, rounding) can pick an equally clear but different assignment. Example: a 1,223 m tram flipped between two member mixes on every re-fit. A clear line is final. `LiftTowerEdit` holds the engine-side numbers (node elevation per member, the vanilla score /
top-anchored placement replica, the power-line edge curve). Tests: LiftBatch LP-11..LP-15.

Never worse over a built line (FIX-LIST 52): `ToWrite(asBuilt, fit, built)` is the plan the fit writes. Over a built line, the fit writes
an unsound plan (`LiftTowerPlan.Sound`: every span clear and no tower over its cap) only when `LiftTowerPlan.WorseThan(plan, asBuilt)`
finds it no worse on every axis:
- Min floor margin (`WorseMarginTolM` 0.05 m).
- `ViolatedSpans`.
- Towers over cap.
- `MaxCapExcessDeg`.

Null = keep the line as built (LiftTowerFitSystem.md). Test: LiftBatch EI-52.

## LiftTowerEdit: the engine-side numbers of a fitted node (LP-15)
A tower node is raised so that the vanilla placeholder ladder spawns exactly the planned member on the ground:
- node y = center ground + E, `Game.Net.Elevation` = (E, E), E = TopM[member] - TopM[0].
- `SubObjectSystem` scores member m' with spare = m_Size.y - (cmin(Elevation) + spec y) (SubObjectSystem.cs:980, :2189-2199, with
  spec y = TopM[0] in all four line prefabs: FG 9.014, DQ 9.95, GD 12, tram 30). So the planned member has spare 0 and score 1, and every
  other member scores at most 1/(1 + step): no tie, no random draw (:2346-2358). A node tagged Updated is re-scored (owner query All SubObject +
  Any Updated, :3936-3943).
- The engine places a Standalone pillar TOP-anchored (AnchorTop + OnGround, :2464-2469). pivot = node y + spec y - member top (:2477-2482) = ground + E + TopM[0] - TopM[m] = the ground. (The node-y-minus-elevation rule at :2521 skips AnchorTop rows.)
- The rope lane = node y + lane y (LaneSystem.cs:4112-4115) = ground + TopM[m] - RopeBelowTopM = the planner's rope.
- The edge curve is the engine's own power-line curve between the two node points, `NetUtils.StraightCurve(a, d, net m_Hanging)`
  (NetUtils.cs:178-191, and CourseSplitSystem.cs:1702-1704 for StrictNodes nets).
- The fit leaves alone a node up to the "above" tolerance over its target. Reason: every write re-curves the node's edges. The carve (cut
  ceiling = edge y + m_HeightRange.min + m_MinHeightOffset = edge y + 4) follows the curve. On steep tram spans the cut reaches the node,
  so the ground under it sank 0.39 -> 0.33 -> 0.27 m per write (ratio ~0.85). A fit that re-grounded on every 0.25 m wrote three times
  and hit the loop guard.
- Over a BUILT line (FIX-LIST 57) the "above" side is gone. The fit judges a built tower at its foot (`BuiltFootSampler`, max(ground,
  node y - E), and `LiftTowerPlanner.FootAlong` keeps a tram float from counting twice). `HeightWrite(old, target, built)` writes only a
  node below its target. The load re-fit re-grounded a tram T60 1.05 m lower once our cut sank the ground under it
  (LiftTowerFitSystem.md "A built tower's ground is its foot", LiftBatch RL-57, RL-1).
- Twins (B2): `ElevationM(c, member, head)` = Top(twin) - TopM[0], so the twin scores 1 (spare 0) and every other member less (tops
  >= 0.1 m apart, check-lift-align LADDER). `EngineWinner(c, e, out head, out score)` scores the ladder and the twins.
  `EnginePivotAboveGround(c, member, head, e)` = 0 for the planned head (LiftBatch HD-4). `MemberOf(c, e, out head)` reads a node back:
  the ladder member or twin within 0.05 m, else the tallest ladder member not above E with the Line head. Every twin E is within the
  line's m_ElevationRange.max (FG 12.5 <= 16, DQ 10.5178 <= 11, GD 8.568 <= 28). DQ raised 10 -> 11 for the DQ20 Hold twin (art-601): under
  10, vanilla `FixElevation` would drop the link at that node (NetToolSystem.cs:3730-3746). Beyond the max, a later vanilla edit is
  unverified.

## Own cut (the trench the fit must not mistake for ground)
A lift line is a FlattenTerrain power line. `TerrainSystem` cuts the terrain down to a ceiling of edge y +
NetCompositionData.m_HeightRange.min + TerrainComposition.m_MinHeightOffset (`AddSegment`, TerrainSystem.cs:1397-1516) and never fills. Our pieces have min 4 (DQ 3.8) and 0, so
the ceiling = edge y + 4. Before the 2026-11-05 collision box round, the pieces had {6, 20} and -2. The cut is live. The game draws it into the height cascade from the base
heightmap on every update of the area (`DrawHeightAdjustments`, :4727-4800, and the brush path writes the base map, :3950). The CPU
heights that the fit samples are read back from the cascade (:3598-3646).
- A vanilla line drawn on the ground runs its edges up to 15 m under convex terrain, so its cut is meters deep. The fit sampled that as the
  ground and stood towers in the trench (node y = cut floor + E).
- Once the old edges were replaced, the trench refilled around the towers (the ground at a fitted tower rose +0.5 / +0.4 / +1.9 m after the
  write). A node left more than 4 m under the ground keeps its own round cut: pits.
- Rule: a sample that sits at this line's own cut ceiling (within OwnCutTolM) is replaced by the plane through the ground just outside
  the cut (lateral +- (half width + CutFarMarginM)), when that plane is higher. Natural ground is never exactly there, and the cut never
  fills. Ground below the ceiling (a valley the rope crosses) is the player's and is kept.

## Tram span: 3 km (TramMaxSpanM)
Peak 2 Peak's record span is 3.03 km. Only the lift-line tool builds a span this long. While the tool is open, it raises the line prefab's
max edge length to this value (`LiftLineToolSystem.SetSpansRaised`). The vanilla net tool keeps SkiTramHV `m_MaxPylonDistance` 1,000 m.
Limits checked for one 3 km edge:
- Course split follows the raised max (CourseSplitSystem.cs:3487-3493). Validation flags a Temp edge longer than 1.1 x max (LongDistance,
  ValidationHelpers.cs:1395-1425), 3,300 m while open. The game refuses a Temp copy of a built span longer than 1,100 m, when the copy is made while the tool is CLOSED. Example: relocating a tram station whose line has a long span. The game never re-validates permanent edges.
  Workaround: rebuild the section with the lift tool.
- Elevation segmentation (CourseSplitSystem.cs:2884-3040, `m_ElevatedLength`) is off while the tool is open, because ShoreLine gates it.
  `NodeReductionSystem` merges collinear Temp edges only for nodes with `Temp.m_Original`, so fresh tool nodes never merge.
- Rope sag is linear in length (4*d*h*t(1-t), LaneSystem.cs:4128-4134): 12 m mid-span at 3 km with the tram's 0.004, exact at any length.
  Every span is tested at every 10 m sample with the sag, floor = cabin bottom (rope - 10.5 m drop) >= 5 m over the high side.
- Terrain cut: ceiling = chord + 11.75 for the tram, and a clear plan keeps the ground <= chord + 10.75 - sag, so a clear span never cuts.
- Rope rendering: the vanilla cable lane mesh has `m_TilingCount 0` and `m_GeometryTiling false`, so `BatchDataHelpers.GetTileCount`
  returns 1 (BatchDataHelpers.cs:581-598). The game deforms one mesh along the whole lane. Each chord deviates ~0.6 m from the parabola,
  invisible.
- Cabin path: `LiftLoopCache.SampleSegments` uses `MathUtils.ClampLength` (16 chords, linear in t inside a chord). The position is always
  on the rope, and only the speed mapping is approximate (negligible, the arc table is double).
- After a load, the tool registry is gone and `LiftTowerFitSystem` passes engineMax = 1,000. Hysteresis keeps a clear line as built. A 3 km
  span that stops clearing (terraformed) is Short.
- The fewest-towers objective hangs a tram over a 1-2 km valley with no tower at all, as real 3S lines do. Planner cost: LP-10 tram 3 km
  rolling 9.9 -> 23.6 ms, LP-25 ridge 43.6 ms for 3 plans. The tool re-plans once per edit, not per frame.

## LiftTowerNumbering
The number on a tower's plate is its height class baked into the prefab texture (`plate_numeral` in the tower build script: FG8 -> 8,
DQ12 -> 12, GD20 -> 20). So a line that mixes members would read 12, 16, 20, 12, ... instead. Order rule: tower n = the n-th line tower by
distance along the line from the base station, 1 at the base, dense (nodes without a tower are skipped). The code computes it from
geometry only, so it is the same after a save / load. An edit renumbers only the towers after the first changed one. The tile objects
themselves: `TowerNumberTiles.md`.

### Tram saddle cap and pedestals (DECISIONS 600 a)
- `SaddleHalfCapDeg` (tram `TramBendHalfCapDeg` 20, +inf otherwise, `With(saddleHalfCapDeg:)`). `LineCapDeg` / `CapDeg` without trains =
  2 x it. So the angle pass, `IsOverCap`, `OverCapTowers`, Sound and the red tower treat a tram saddle like a chair train. Keys: PLAN_SADDLE /
  TOWER_SADDLE (not TRAIN: a saddle has no wheels).

  PC-7 table (TS-1, 132 lines, towers 253 with no cap):
  - Cap 20: 25 lines take a breakover pair (+25 towers), 30 still warn.
  - Cap 17.5: 14 / 48.
  - Cap 15: 9 / 59.

  A tighter cap binds more lines that a pair cannot fix. FB-3 trams never bind (widest half 14.1). Steep set: 1 pair at 20, none warn.
- `SetFloats` (Build, after SetLeans, then SetLeans again): every leaned tram tower gets `FloatM` = `LiftAlignMath.TramFloatM` on the center
  profile (`CentreAt`, downhill = toward the lower neighbor). RopeM and the anchor foot rise with it. `NodeY` adds it. The search, the angle
  pass and the bend (SetAngles) stay on the unfloated ropes. Writers: LiftLineEdit.Emit and LiftTowerFitSystem put the node at ground + E +
  FloatM (HeightWrite's target includes it). In the sweep sets, no tower needs it (the floor is 0 wherever the ground falls with the lean).
  It fires on a level spot under a 50+ deg rope, for example PC-8's bench: T60, lean 25, float 1.34 m.
- Leaned spans may reach samples past their nodes (a 25 deg FG20 moves its anchor about 8 m). So Build and LeanedSpanViolation judge the floor
  over the anchors' range, not only between the nodes (PC-5).

### Carrier clearance (DECISIONS 600 c)
- `LiftPlanNode.HalfDeg` (AnglesAt: `LiftAlignMath.WorldHalfDeg` on the drawn rope, plumb rope points). `LiftTowerPlanConstants.WorldHalfMaxDeg(head)`
  (cached per head from `LiftAlignMath.WorldHalfTable`). `WorldHalfOn`, `With(worldHalfOn:)` turns it off for the before / after sweeps.
- The angle pass (`Over`) adds `half - WorldHalfMaxDeg(kind)` to the bend and pitch terms. It plans members, not heads, so a kind stands for
  the head it takes (HoldDown -> Hold, PushPull -> Combo, Support -> min(Line, Break)). The load cases run only when the bend or the half
  could bind (`MinWorldHalfMaxDeg`). A tower past it gets the usual repairs (shift / member, breakover pair, longer span). The planner
  refuses a pair whose new tower is past it, like any over-cap candidate.
- Build judges the PLACED head. The effects:
  - `IsOverClearance(i)`, `ClearanceTowers`.
  - Not Sound.
  - Verdict / OverCapNote "PAST CLEARANCE".
  - WorseThan axis.
  - AngleText "half H PAST CLEARANCE L".
  - Keys: the plan line PLAN_CLEARANCE (after PLAN_PITCH), the tower TOWER_CLEARANCE (after TOWER_PITCH, DEG = HalfDeg, LIMIT = the limit,
    one decimal).
  - The red tower in the overlay.
- Bite (PC-9, all sets, the rule off vs on): 3 of 450 crossable chair lines newly warn (0.67 %). All are crest-end FG g 0.30 back 20. The
  base-station exit (station rope 1.4 m under the ground, the ground at 0.3) needs a first tower whose arriving rope is 46-51 deg. The pitch
  cap used to fix it with a pair at 15 / 35 m whose first tower carried the 50.7 deg half. The planner now refuses that pair, and the single
  tower stays OVER CAP and PAST CLEARANCE. (These plans end with one tower fewer than the uncapped plan, so PC-3 now checks "no added tower",
  not "the same count".) FB-3: no tower past it (steepest halves FG 40.5 / DQ 36.7 / GD 36.5 deg).
