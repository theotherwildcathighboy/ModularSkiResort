# LiftServedRule: when a run counts as lift-served, and the power predicate with its flicker guard

The lift-served rule and the power predicate. Consumers: `SkiResortAttractivenessSystem` (served runs, lift value), `SkiResortStatsSystem`
(servedBy, runsServed), `SkiLiftChargeSystem` and `SkiLiftTicketSystem` (IsPowered), via `LiftCensusSystem.CopyServeLifts` / `ReadLiftPower`;
tests/LiftBatch and tests/ResortBatch link it. The census keeps edge to row only, so a line is a set of chords, not an ordered polyline; a
complete row may carry a spur.

## The rule
A run is served by lift L when:
1. L's census row is Complete (Open / Unfinished / StationsOnly serve nothing: a section being redrawn drops service until the line
   closes);
2. `IsPowered(L.Power)`: not NoPower;
3. the run's top T (RunFacts.TopNode, the highest outline node) satisfies T.y <= TopY(L) + TopMarginM, TopY = the top station's stub-tip y;
4. the xz distance from T to the nearest chord of L (each SkiChairliftHV line edge, Edge.StartPos.xz to Edge.EndPos.xz) is <= ServeRadiusM.

Both bounds are inclusive. The serving lift is the nearest; an exact tie goes to the lower EntityRef (Index, then Version). Applies to all
four run kinds. A curved section is measured along its straight chord (error unverified).

A run is also served when linked to a served run (outline nodes within LinkMeters, or overlapping outlines, same ski area; transitive):
`AssignLinked`, `RunLinks`. Rules 1-4 decide direct service.

## Per lift type
`Lift` carries Rule (ServeRuleKind as a byte), RadiusM, TopMarginM and TopXZ (the top station's HV stub tip xz, NaN when the top is not in
the last build), filled by `LiftCensusSystem.CopyServeLifts` from the `SkiCatalog.Lifts` entry:
- AlongLine (fixed-grip, detachable, gondola: radius 150, margin 15): rules 3-4 with the row's numbers.
- TerminalsOnly (tram: radius 300, margin 15): the run top within RadiusM (xz, inclusive) of TopXZ and T.y <= TopY + TopMarginM; distanceM
  is the xz distance to TopXZ.

A Lift built without the new fields reads RadiusM <= 0 as `SkiCatalog.FixedGrip.ServeRadiusM` and TopMarginM <= 0 as FixedGrip.TopMarginM,
so older oracles keep their meaning. A mixed or untyped census row serves nothing because it is never Complete. `SummitServed`: a lodge is
summit-served when its xz lies within radiusM (inclusive) of the TopXZ of a lift that Serves; the caller passes the lifts of the lodge's
ski area.

## Power
`PowerOf` maps ElectricityConsumer.m_Flags (ElectricityConsumerFlags.cs:5-12) to NoPower exactly when the game draws its electricity icon
(NoElectricityWarning or BottleneckWarning, DispatchElectricitySystem.cs:126-158). `IsPowered` is the only "powered" predicate in the mod;
PowerGatesService = false turns power into status only everywhere. The census reads the 4-argument PowerOf: NoPower only while the warning
shows and under half of the wanted power arrives, back at three quarters or when the warning clears (hysteresis); the game warns at any
shortfall after two short dispatches (:126-142). An unpowered lift of every type carries no guests, does nothing for the resort and its
carriers stop; a lift is powered from its base station only (the top is never read).
- `Carries(complete, power, closed)` is the run predicate: complete and IsPowered(power) and not closed. Call sites: ChairPin's hold gate
  (`LiftLoopHold.GateOf`), the hub's capacity (`HubRules.LiftCapacityShown`), the tram's Held gate. Older equivalent expressions
  (Lift.Serves' power factor, `SkiMoneyRules.LiftOperating`, the score's Complete && Powered) stay as written; tests pin the equivalence
  (LiftBatch PG-1, ResortBatch HK-3). The bool overload takes the hub's shown power (Power != NoPower).
- `GuardPower` is the flicker guard, applied by the census's one power reader (`LiftCensusSystem.ReadLiftPower`) to PowerOf's result:
  NoPower at once, Powered again only after PowerBackHoldFrames (512) sim frames of raw Powered reads (SimulationSystem.frameIndex,
  SimulationSystem.cs:68; a dispatch every 128 frames at offset 126, DispatchElectricitySystem.cs:222-230). A station whose effective power
  was never NoPower passes through untouched. A reader polling every 64 frames can see a one-dispatch (128-frame) "warning and under half"
  window that 256-frame pollers may miss; that lift then reads NoPower for at least 512 frames (LiftBatch PG-2 h).
- `ExpireStreak`, for a caller that tracks its last read frame per station: a Powered streak whose station went unread for more than
  PowerStreakStaleFrames is dropped, so a lift redrawn mid-streak waits a fresh 512.

## Pure file
`using System;`, `System.Collections.Generic`, `Unity.Mathematics` and EntityRef only (the test-host rule), plus SkiCatalog consts
(ServeRadiusM and TopMarginM alias SkiCatalog.FixedGrip, compile-time literals; SkiCatalog.cs is linked wherever this file is). No state;
Assign is allocation-free once its lists have grown.

## Near AssignLinked
The served rule with run-to-run links; the attraction path and the stats path both call it, so the two cannot disagree.
- Runs `Assign` first: servingLift / servingDistanceM / servedCount keep their meaning (the DIRECT test of each run's top against the
  lifts; a lift's runs-served count stays direct only).
- Then a breadth-first pass over `links` (Build it first; a graph of another size is ignored, i.e. no links). A run is SERVED when its own
  top passes (servedRoot[i] = i, servedVia[i] = -1) or it is linked, through any chain of linked runs (each run is visited once, so the
  chain is bounded by the run count), to a directly served run (servedRoot[i] = that directly served run, the nearest by link count, ties
  to the lower run index; servedVia[i] = the run it was reached through, one link nearer the root). Unserved: both -1.
- Output lists are cleared and filled; allocation-free once grown.
- `leafRuns` (null = none) marks LEAF runs by index. A leaf may be served (directly by a lift, or through a link from a served neighbor)
  but is never expanded by the pass: it never passes service on, so a cat track that crosses a black run does not "serve" it. A list
  shorter than the run count reads false for the missing indices.
