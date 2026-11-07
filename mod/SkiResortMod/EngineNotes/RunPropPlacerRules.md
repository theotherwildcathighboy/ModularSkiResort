# RunPropPlacerRules: where a run's guns, hydrants and masts stand, walked by distance along its border

Pure rules behind `SkiRunPropSystem.Placer.cs`. Tests are in `tests/ResortBatch/PlacerTests.cs`. Citations are the `Game.dll` 1.6.0f1 decompile baseline.

## Why a walk

Vanilla area props stand only on outline nodes, one roll per node (`SecondaryObjectSystem.cs:2972-3007`, `:3279`). A 4-corner run shows 0 or 1 floodlight. A generated cat
track (nodes down to 2 m apart, `GradeRibbon.MinSpacingM`) would clump on bends and leave gaps on straights. The placer's result does not depend on how the outline was drawn.

## The canonical ring

`PlacerRing` drops repeated nodes (1 cm), makes the ring counter-clockwise in x,z and starts it at the lexicographically smallest (x, z) vertex. So a start node, a winding or
extra nodes on a straight side change nothing. A ring is not valid for fewer than 3 distinct nodes, no enclosed area, or a NaN or infinite coordinate. A ring that is not valid
plans nothing.

## Slots

- Closed ring (snowmaking, night): N = round(perimeter / spacing), at least 1, at perimeter / N, so the seam shows no gap. Guns take slot 0 on the canonical start. Masts
  take the half-step phase, away from the guns.
- Chain (cat track): the same count over the chain's length, half a step off each end.
- A gun or hydrant closer than `MastClearM` to a mast, along the ring, moves to that distance past the mast (at a chain's end, back).
- Heading: a chord of `TangentWindowM` to each side, so a node next to a slot cannot swing it. Guns and hydrants head along the border. Masts face `MastAimRules.AimFromTangent`
  (inward plus a lean toward downhill, from that chord's fall rate).

## A cat track's side

`SideChain` looks for the ribbon's two caps (`GradeRibbon.Build`: left edge forward, right edge back, two straight ends). Nodes turning under `CollinearDeg` are ignored. A cap is
an edge between corners whose turns add up to `CapMinTurnDeg` or more with the ring. The two shortest caps that are not neighbors win. The chain is the arc holding the canonical start
vertex, so the same ribbon always takes the same side. A ring without two caps splits at its two farthest corners.

## The mix

`Props` has the prefabs of the vanilla groups (`SkiRunSnowmakingBorder`, `SkiRunNightBorder`) with their `m_Probability` weights, without the empty marker `SkiPropNone`: the spacing
is the density now. Guns and hydrants keep 110 : 40, masts 100 : 28 : 22. `Pick` hashes a seed from the canonical start vertex and the kind with the walk and the slot. So an edit
elsewhere on the ring keeps the mix. `tools/check-run-props.js` compares the table with the prefabs.

## Defaults

Off, then `SpacingMinM`..`SpacingMaxM` in `SpacingStepM` steps. A hand-drawn or Quick run with many nodes has a node about every 40 m (an estimate, not measured). Quick runs simplify
at 12 m. The corner roll stood a gun or hydrant on 75 % of the snowmaking nodes and on 43 % of the night nodes. It stood a mast on another 43 % of the night nodes. So 40 / 0.75 = about 50 m on snowmaking, and
40 / 0.43 = about 90 m on night. A cat track has no such reference: guns at 60 m, lights off.

## The diff and the key

`Diff` keeps a standing prop with the same prefab, within `KeepPositionM` and `KeepYawDeg` of a planned one. So a rebuild leaves unchanged stretches alone. `RingKey` hashes what a plan
depends on at 0.1 m (`PlanVersion`, kind, spacings, canonical x,z). It leaves heights out because the game re-snaps these props to the terrain itself (`GroundHeightSystem.cs:229`, `:604`).
Extra nodes change the key but replan to the same props: no op, no pop.

A ring whose lexicographic minimum changes (a node moved past the leftmost one) starts its closed-ring walk elsewhere. So that edit re-places more than the moved stretch. This is rare and harmless.
