# LiftAlignMath: plan bends, rope slopes at a node, sheave-train pitch

Pure math of the lift alignment fix (2026-11-02): why the haul rope crossed the tower heads and the carriers at an angle, and what
the code does about it. No Game or Entities type, so `tests/LiftBatch` links it (`AlignTests.cs` AL-1..AL-11, `BreakoverTests.cs`
AL-12..AL-14). Engine citations are `File.cs:line` against the Game.dll decompile baseline 1.6.0f1.

## The rope a node sees
- A rope lane between two anchors is the engine's hanging lane: the chord a..d with the inner control points at 1/3 and 2/3 lowered
  by `|a.xz - d.xz| * m_Hanging * 4/3` (`LaneSystem.cs:4128-4138`; `LiftTowerEdit.HangingCurve`). A cubic bezier's end tangent is
  3(b - a), so the rope LEAVES a at the chord slope minus 4h and ARRIVES at d at the chord slope plus 4h (`LeaveSlope`,
  `ArriveSlope`). A tower between two spans therefore sees the change of chord slope plus 8h of break-over.
- h per type = the catalog's `Envelope.LaneHanging` (`SkiCatalog.cs`, the rope sag round): FG 0.02 (own `SkiFixedGripHaulRope`
  lane), DQ / GD 0.025, tram 0.04. (Before the sag round: FG 0.01, the others 0.004; the vanilla HV lane's 0.03 once gave the FG
  rope 12.6 deg of break-over per tower on 60 m spans.)

## Train pitch
- `TrainPitch(prev, node, next, h, max)` = the bisector of the arriving and leaving rope ANGLES, clamped to the line prefab's
  `m_MaxSlopeSteepness` angle. On a uniform slope with no sag it is the slope exactly; with sag it is within 0.3 deg of it at
  h 0.03 (atan is not linear), and the outer sheaves are off the rope by only `xo * tan(break-over / 2)` (what real rockers absorb).
- `StationTrainPitch(station, line, h, max)`: the first span's rope angle where it leaves the station anchor.
- `PitchedRotation(yaw, prev, next, pitch)`: a node's rotation is yaw only (`NodeAlignSystem.cs:209 / :212`, the bisector of the two
  line directions in plan), and the node may face either way along the line, so the sign of the pitch about local x follows
  `dot(forward, next - prev)`. Unity's `RotateX(+a)` turns +z toward -y (nose down): nose up along +z is `RotateX(-a)`.
- `AboutPin(pivot, rotation, local)`: a point fixed on the pitched object, relative to the pin it turns about. With the rope anchors on
  the train and the pin on the rope line the pitch is exact (no crown drift).

## Rope angles, train kinds and caps (breakover / hold-down phase B1, 2026-11-03)
- `RopeAngles(prev, node, next, h, out in, out out)`: in = atan(ArriveSlope(prev, node)), out = atan(LeaveSlope(node, next)), radians.
  The ONE place a rope angle at a node is computed: `TrainPitch` is their bisector (AL-12 checks 1,000 random triples), the planner's
  phase (d), the plan output, the tool log and the fit log all call it (through `DeflectionDeg` / `LoadCaseDeflections`).
- `DeflectionDeg` = degrees(in - out): + convex (the rope presses on support sheaves), - concave (it lifts: hold-down sheaves). The
  same value either way along the line. Hand values (AL-12): level, equal neighbors 2 atan(4h) = FG 9.15, DQ / GD 11.42, tram 18.18;
  a uniform slope g gives atan(g + 4h) - atan(g - 4h), LESS than the level value (FG at g 0.5: 7.33), so "2 atan(4h) on every tower"
  holds on level ground only; FG crest 0.3 -> 0 over 60 m spans +25.38; FG dip -0.3 -> +0.3 -24.81.
- Load cases: the rope's sag ratio scales with load / tension, so the planner judges two: tight (empty line, h / 1.4) and heavy
  (loaded, h x 1.5625). `ClassifyTrain(tight, heavy, previous)`: Support while tight >= +1 deg (`MinPressDeg`, the least press
  that keeps the rope seated), HoldDown while heavy <= -1 deg, else PushPull (a combination train: the rope can lift off either way).
  Band edges for symmetric chords (AL-13): FG support down to -5.5 deg of chord change, hold-down below -15.2; DQ / GD -7.2 / -18.7.
  Hysteresis `KindHystDeg` 0.25: the previous kind is kept while it is still valid within 0.25 deg (the fit logs per tower; never
  saved).
- Caps: `PerWheelMaxDeg` 4.0 (0.07 rad per wheel, D.M. 8 March 1999, 3.18.3) x the wheels per side, at most 40 (the art's fold,
  2 x `MaxHalfBreakDeg`): `TrainCapDeg(wheels)`. Wheels per side from the tower builder (lift_tower.py): FG 4 / 4 / 2, DQ 8 / 8 / 4,
  GD 10 / 8 / 4 (support / hold-down / combination), so caps FG 16 / 16 / 8, DQ 32 / 32 / 16, GD 40 / 32 / 16 (AL-14). The tram has
  no trains and no cap: its saddle's bare-rope break-over (h x 0.75, `BareSagScale`) under 1.15 deg (0.02 rad, decree 2012, 14.4.4,
  `SaddleMinBreakDeg`) is logged only.
- `LiftTrainKind` = None / Support / HoldDown / PushPull (None: not judged, a station end or a tram saddle).

## Train limits
- `TrainOverDeg(front, nF, back, nB, max)`: degrees the rope asks past the train, from the UNclamped half pitches (HalfPitches'
  inputs): the larger of a half's pitch past `max` (TrainPitchLimit, 30 deg at most) and the half-break past `MaxHalfBreakDeg` (20 deg
  per half, 40 between the halves). 0 within both, or with no rope. `TrainWarnDeg` 0.5: the hub's LiftTrainLimit threshold, so a tower
  sitting right at a limit never raises it. Tests: LiftBatch AL-11.

## Plan bends
- `PlanBendDeg(prev, node, next)`: the turn angle in plan, 0 = straight. The tower's train sits on the bisector, so the rope meets it
  at half the bend on each side; at 5 deg the outer sheaves are off by FG 0.034 / DQ 0.090 / GD 0.122 m. `BendWarnDeg` = 5: the lift
  tool and the hub warn above it (owner verdict 2026-11-02: warn, never refuse; real lines turn in angle stations).

## Carrier drop (in LiftMoverMath)
`LiftMoverMath.CarrierDrop(gripTop, gripTopAboveRopeM)` = grip top minus the jaw's rope point: the rope runs through the jaw, not over
the grip top (F5, reverses round-2 contract D8). Every pose of a carrier uses it (ChairPinSystem pin and mover through the catalog's
`CarrierDrops` view, the tram). Gates: LiftBatch AL-1, `tools/check-lift-align.js` PINDROP (preflight ALIGN, CI).
