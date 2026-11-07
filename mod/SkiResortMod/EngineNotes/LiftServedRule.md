# LiftServedRule: when a run counts as lift-served, and the power predicate with its flicker guard

This file covers the lift-served rule and the power predicate. These consumers read them via `LiftCensusSystem.CopyServeLifts` /
`ReadLiftPower`:

- `SkiResortAttractivenessSystem` (served runs, lift value)
- `SkiResortStatsSystem` (servedBy, runsServed)
- `SkiLiftChargeSystem` and `SkiLiftTicketSystem` (IsPowered)

tests/LiftBatch and tests/ResortBatch link the file. The census keeps edge to row only. So a line is a set of chords, not an ordered
polyline. A complete row may carry a spur.

## The rule
A run is served by lift L when:
1. L's census row is Complete. Open / Unfinished / StationsOnly serve nothing. So a section being redrawn drops service until the line
   closes.
2. `IsPowered(L.Power)`: not NoPower.
3. The run's top T (RunFacts.TopNode, the highest outline node) satisfies T.y <= TopY(L) + TopMarginM. TopY is the top station's stub-tip y.
4. The xz distance from T to the nearest chord of L (each SkiChairliftHV line edge, Edge.StartPos.xz to Edge.EndPos.xz) is <= ServeRadiusM.

Both bounds are inclusive. The serving lift is the nearest. An exact tie goes to the lower EntityRef (Index, then Version). The rule applies
to all four run kinds. A curved section is measured along its straight chord (error unverified).

A run is also served when a skier can get onto it DOWNHILL from a served run (2026-10-25). These conditions apply:

- The outlines are linked (nodes within LinkMeters, or overlapping, same ski area).
- A junction exists that the skiers reach.
- A real part of the run lies below the junction (BelowJunction).

Cat tracks carry service along themselves, downhill or level only. See `AssignLinked` and `RunLinks`. Rules 1-4 decide direct service.

## Per lift type
`Lift` carries Rule (ServeRuleKind as a byte), RadiusM, TopMarginM and TopXZ (the top station's HV stub tip xz, NaN when the top is not in
the last build). `LiftCensusSystem.CopyServeLifts` fills them from the `SkiCatalog.Lifts` entry:
- AlongLine (fixed-grip, detachable, gondola: radius 150, margin 15): rules 3-4 with the row's numbers.
- TerminalsOnly (tram: radius 300, margin 15): the run top is within RadiusM (xz, inclusive) of TopXZ, and T.y <= TopY + TopMarginM.
  distanceM is the xz distance to TopXZ.

A Lift built without the new fields reads RadiusM <= 0 as `SkiCatalog.FixedGrip.ServeRadiusM` and TopMarginM <= 0 as FixedGrip.TopMarginM,
so older oracles keep their meaning. A mixed or untyped census row serves nothing because it is never Complete. `SummitServed`: a lodge is
summit-served when its xz lies within radiusM (inclusive) of the TopXZ of a lift that Serves. The caller passes the lifts of the lodge's
ski area.

## Power
`PowerOf` maps ElectricityConsumer.m_Flags (ElectricityConsumerFlags.cs:5-12) to NoPower exactly when the game draws its electricity icon
(NoElectricityWarning or BottleneckWarning, DispatchElectricitySystem.cs:126-158). `IsPowered` is the only "powered" predicate in the mod.
PowerGatesService = false turns power into status only everywhere. The census reads the 4-argument PowerOf. It gives NoPower only while the
warning shows and under half of the wanted power arrives. It returns to Powered at three quarters, or when the warning clears (hysteresis).
The game warns at any shortfall after two short dispatches (:126-142). An unpowered lift of every type carries no guests and does nothing
for the resort, and its carriers stop. A lift is powered from its base station only (the top is never read).
- `Carries(complete, power, closed)` is the run predicate: complete and IsPowered(power) and not closed. Call sites are ChairPin's hold
  gate (`LiftLoopHold.GateOf`), the hub's capacity (`HubRules.LiftCapacityShown`) and the tram's Held gate. Older equivalent expressions
  (Lift.Serves' power factor, `SkiMoneyRules.LiftOperating`, the score's Complete && Powered) stay as written. Tests pin the equivalence
  (LiftBatch PG-1, ResortBatch HK-3). The bool overload takes the hub's shown power (Power != NoPower).
- `GuardPower` is the flicker guard. The census's one power reader (`LiftCensusSystem.ReadLiftPower`) applies it to PowerOf's result. It
  gives NoPower at once. It gives Powered again only after PowerBackHoldFrames (512) sim frames of raw Powered reads
  (SimulationSystem.frameIndex, SimulationSystem.cs:68). A dispatch comes every 128 frames at offset 126 (DispatchElectricitySystem.cs:222-230).
  A station whose effective power was never NoPower passes through untouched. A reader that polls every 64 frames can see a one-dispatch
  (128-frame) "warning and under half" window. 256-frame pollers may miss that window. That lift then reads NoPower for at least 512 frames
  (LiftBatch PG-2 h).
- `ExpireStreak` serves a caller that tracks its last read frame per station. It drops a Powered streak whose station went unread for more
  than PowerStreakStaleFrames. So a lift redrawn mid-streak waits a fresh 512.
- **Pending: the start no longer fails open (2026-11-05, DECISIONS 601 i, owner note 7).** The game warns only on the SECOND short
  dispatch (`kAlertCooldown` = 2, DispatchElectricitySystem.cs:208, HandleCooldown :128-143). It sets the wanted power at offset 0
  (AdjustElectricityConsumptionSystem.cs:385-392), before the dispatch at offset 126. So a fresh unpowered station showed no warning for up
  to ~380 sim frames and read Powered. Its chairs ran a few seconds after an unpause, then stopped. The census reads the 5-argument
  `PowerOf` (with `m_CooldownCounter`). These rules apply in order:
  1. A warning -> the 4-argument rule.
  2. Connected with fulfilled >= wanted > 0 -> Powered. (A dispatch with wanted 0 sets Connected too, so Connected alone is stale.)
  3. A station read Powered or NoPower before -> Powered. (The game's cooldown absorbs one short dispatch, as before.)
  4. Else, with no powered history: a short dispatch (cooldown > 0) takes the half rule at once. Anything undispatched reads `Pending`.

  Pending is not powered (`IsPowered` false: the chairs and the tram wait) but shows as no outage (`Power != NoPower` in the hub and the
  stats). `ExpirePending` fails it open after `PendingMaxFrames` (384 = 3 dispatch periods). Only a city whose electricity is locked
  (HandleCooldown never runs, :102-105) waits that long. A fresh powered lift reads powered at its first judged dispatch, at most 254 sim
  frames after the build (about 4 s at 1x, 1.4 s at 3x). LiftBatch PG-5 models the dispatch over all 128 build offsets. Logs:
  `Lift [power]: ... power pending`, `... powered at its first electricity dispatch after N sim frame(s)`, or the fail-open line.

## Pure file
The file uses only `using System;`, `System.Collections.Generic`, `Unity.Mathematics` and EntityRef (the test-host rule), plus SkiCatalog
consts. ServeRadiusM and TopMarginM alias SkiCatalog.FixedGrip, as compile-time literals. SkiCatalog.cs is linked wherever this file is.
The file has no state. Assign is allocation-free after its lists grow. RunLinks holds the graph and AssignLinked's scratch (one instance
per caller).

## Near AssignLinked
The served rule with downhill run-to-run links. The attraction path and the stats path both call it, so the two cannot disagree.
- `AssignLinked` runs `Assign` first. servingLift / servingDistanceM / servedCount keep their meaning (the DIRECT test of each run's top
  against the lifts). A lift's runs-served count stays direct only.
- Inputs per run:
  - The outline xz.
  - The node heights in the same order (`RunLinks.Build(outlines, heights, areas)`). Null, or another length, reads as flat, y 0.
  - The ski area id.
  - The cat track mask (`catTracks`, RunKindRules.IsCatTrackLink = RunKind.CatTrack, null = none).

  Node y is the area node's terrain-snapped height. GroundHeightSystem re-snaps it (AreaUtils.cs:672-677), and the catalog's YKey hashes it.
  It is the same data as the run top's y in rule 3. The graph stays while every outline AND height array is the same object.
- Contacts (`RunLinks.ContactsOf`, per direction of a link) are the places where a skier can pass from one run to the other, with the
  place's height on each run. There are three kinds:
  - Near nodes: per node of the source, the node of the target within LinkMeters that a skier boards highest (the most of min(both
    heights), a tie to the lower target node).
  - Crossing edges: the crossing point, with heights linear along each edge.
  - A node inside the other outline: that node's height on both sides (same ground).

  Each contact carries the nearest node on both runs (for the cat track walk).
- The pass is a work list from the directly served runs (index order). Every served run carries a CAP: the highest a skier on it gets
  (+inf for a directly served run). A contact is usable from a plain run when its height there is at most the run's cap. From a cat track,
  a contact is usable when its node is reached. The skier boards at p = min(height on the source, height on the target). The target's cap
  is min(source cap, p + ClimbToleranceM), so caps never grow along a chain.
- A plain run is served through a contact only when `BelowJunction(p, lowest node y, highest node y)`: p - lowest >= min(MinBelowM 30 m,
  drop / BelowDropDivisor 3). A run touching a served run at its own bottom is not served. A run joined mid-way is served. Service never
  flows up a run past where it was entered (its cap).
- A cat track is entered at any contact whose nearest track node is no higher than the cap. It needs no drop (it is the way across). Its
  skiers reach every node that joins an entry through nodes no higher than that entry's cap. The cap is ClimbToleranceM (3 m) above the
  boarding height. The skiers can go either way along the ring and across the ribbon. The walk graph is ring neighbors plus same-outline
  nodes within LinkMeters. A directly served track is entered at its top node with cap +inf. So a track across the whole mountain serves
  only runs it meets below (or within 3 m above) where skiers get on. A run it meets is served by rule 1 with p at the meeting point.
- Why a cap and not a summed climb: a summed budget restarts whenever the skier steps from the track onto a crossing run and back, so it
  cannot be enforced. The cap can be enforced (ResortBatch cattrack/downhill D-2, the rising track). Accepted limit: a track that dips and
  climbs back up to (not above) the entry height counts as passable.
- servedRoot[i] = the directly served run of the chain that first reached it. servedVia[i] = the run it was first reached through
  (breadth-first order, so with flat input the nearest by link count, ties to the lower index). For an unserved run, both are -1. Without
  heights, every contact is usable and every linked run passes. This is the old link rule (ServedLinkTests SL-1..8 run flat and unchanged).
- Cost (tests/LiftBatch SL-8, 200 runs x 120 nodes, 370 links): cold Build + assign about 140 ms in the test host (the old graph: 113 ms,
  T06). A kept graph takes about 0.3 ms. Output lists are cleared and filled. It is allocation-free after the lists grow.
