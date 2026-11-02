# DensityTests: the settle rule, the prop classes and the mast aim

`DensityTests` covers `RunPropDensityRules` and `MastAimRules`; the walk is in `PlacerTests` (`RunPropPlacerRules.md`). Oracles are literal hand values, never a second run of the same code. Not testable
headless: the ECS writes and the census.

## Sections

- Settle: `SettleFrames` 30, `NextStable` counts up and caps, a change restarts it.
- Classes: `ClassOf` by prefab name.
- Allowed: `PropClassAllowed`, a literal table of kind x (gun, hydrant, mast, mast lit); the empty marker and other never stand.
- Aim: `AimFromTangent` on hand values (inward by winding, half the fall half the lean, the 25 deg clamp, a NaN fall, a zero tangent); `AngleDeg`.

## PlacerTests

Spacing independent of node count, start node and winding; closed ring coverage with the seam; degenerate rings; one-sided straight, bent and hairpin ribbons with the side checked per prop; the two-farthest-corners
fallback; class rules by kind and lit; mast positions and aim; `MastClearM`; the mix over 30000 slots; a frozen `Pick` sequence from an independent plain-JS run of the hash; `RingKey`; `Diff`; billing (`TrackGunsCanRun`,
`TrackBilledHa`, `GunWhyOf` for a track).
