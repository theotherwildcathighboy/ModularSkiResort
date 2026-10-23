# LiftLoopHold: the arithmetic behind stopping and resuming a chairlift loop

An unpowered lift's carriers stop: `ChairPinSystem` holds a loop per station pair (transient, no save data) and uses this arithmetic.
Linked into tests/LiftBatch (LoopHoldTests).

## Chair clock (ChairPinSystem)
Chair i stands at s_i = (offset_i + PhaseOf) % L, PhaseOf = m_SpeedMps x TimeS (ChairPinSystem.cs:1273-1276 move pass, :1381-1384 PhaseOf),
TimeS = (RenderingSystem.frameIndex + frameTime) / 60 in double (:1373-1376).

## Members
- `GateOf`: Run when no census row is known for the loop's station pair, the row is not Complete, or the lift carries
  (`LiftServedRule.Carries`: complete, powered at its base station, not closed; closed = its ski area has no open run, `LiftClosedRule`,
  one game hour of hysteresis); Hold otherwise. Fails open: a loop the census cannot explain keeps running.
- `FrozenPhase`: a held loop stands at s_i = (offset_i + FrozenPhase(speed, FrozenTimeS)) % L, PhaseOf's product at the frozen clock bit
  for bit, so the hold-entry frame writes the pose a running pass would.
- `Rebase`: at the resume every offset becomes (offset + speed x (frozen - now)) mod L in [0, L), so (offset' + speed x now) % L equals the
  frozen s to double rounding (worst measured 2.03e-7 m over the whole uint clock; the test asserts < 1e-6 m, inside MoveChair's 1 mm
  idempotence test, ChairPinSystem.cs:1410-1411). Every offset shifts by the same amount, so spacing is kept. No state: the caller keeps
  FrozenTimeS per held loop.
- `ExpectsLoop`: only a Loop-mover lift owes ChairPin a closed station-to-station loop. A Shuttle (the tram) is moved by
  `TramShuttleSystem` and never harvested by ChairPin, so the "did not close into a loop" WARN must skip it; untyped (null) rows are
  expected to loop.
- `HeldSuffix`: the rebuild signature's held count names why loops are held ("no power" vs "closed").
