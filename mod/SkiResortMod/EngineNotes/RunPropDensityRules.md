# RunPropDensityRules: what a run may carry, the settle rule, and mast aiming

`NextStable` is the settle: a run's slider must stay still for `SettleFrames` updates before re-placement. `ClassOf` names a prop's class by prefab (the load sweep's line).
`PropClassAllowed(kind, class, lit)`: snowmaking guns and hydrants; night all three; cat track guns and hydrants, masts only when lit (its lights slider above Off); others nothing; the empty
marker never. The old Options density formulas are gone with the sliders: the spacing is the density now (`RunPropPlacerRules`).

## Near MastAimRules

The game places an area-border prop with local +Z along the outgoing border segment (`Game.Objects/SecondaryObjectSystem.cs:2984-2992`,
`quaternion.LookRotationSafe`) and never consults the winding (`AreaFlags.CounterClockwise`), so the lamps (prefab `EffectSource`
rotation, pitch 25 deg about X, yaw 0 = +Z) shine ALONG the border and a prefab yaw cannot fix it: the two sides of a run mirror. The
placer stands each mast facing the vector `MastAimRules` returns; nothing rewrites it afterward.

Rules:

- The border's tangent comes from a chord around the slot (`RunPropPlacerRules.TangentWindowM`), so a node beside a slot cannot swing the mast. The ring is counter-clockwise by then; `ccw` is kept for clarity
  and for the tests: inward(d) = ccw ? (-d.z, d.x) : (d.z, -d.x).
- Downhill bias: s = the chord's fall rate along travel (positive = traveling along the border loses height); the aim is inward + clamp(s / `SlopeFull`, -1, 1) x tan(`BiasMaxDeg`) x tangent, normalized. On
  a border along the fall line the lamps lean up to `BiasMaxDeg` toward downhill; across a contour line they aim straight in. A NaN fall reads flat.
- The 25 deg downward pitch stays the prefab's (`EffectSource` rotation x 0.21644, w 0.976296 on all three masts).
- Deterministic: a function of the node ring only.
