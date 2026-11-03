# LiftAlignMath: plan bends, rope slopes at a node, sheave-train pitch

Pure math of the lift alignment fix (2026-11-02): why the haul rope crossed the tower heads and the carriers at an angle, and what
the code does about it. No Game or Entities type, so `tests/LiftBatch` links it (`AlignTests.cs`, AL-1..AL-8). Engine citations are
`File.cs:line` against the Game.dll decompile baseline 1.6.0f1.

## The rope a node sees
- A rope lane between two anchors is the engine's hanging lane: the chord a..d with the inner control points at 1/3 and 2/3 lowered
  by `|a.xz - d.xz| * m_Hanging * 4/3` (`LaneSystem.cs:4128-4138`; `LiftTowerEdit.HangingCurve`). A cubic bezier's end tangent is
  3(b - a), so the rope LEAVES a at the chord slope minus 4h and ARRIVES at d at the chord slope plus 4h (`LeaveSlope`,
  `ArriveSlope`). A tower between two spans therefore sees the change of chord slope plus 8h of break-over.
- h per type: FG 0.01 (own `SkiFixedGripHaulRope` lane since F3; the vanilla HV lane's 0.03 gave 12.6 deg of break-over per tower on
  60 m spans), DQ / GD / tram rope lanes 0.004.

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
