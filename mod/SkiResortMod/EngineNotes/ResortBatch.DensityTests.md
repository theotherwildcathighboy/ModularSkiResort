# DensityTests: the settle rule, the prop classes and the mast aim

`DensityTests` covers `RunPropDensityRules` and `MastAimRules`. The walk is in `PlacerTests` (`RunPropPlacerRules.md`). Oracles are literal hand values, never a second run of the same code.
The ECS writes and the census are not testable headless.

## Sections

- Settle: `SettleFrames` is 30. `NextStable` counts up and caps. A change restarts the count.
- Classes: `ClassOf` works by prefab name.
- Allowed: `PropClassAllowed` is a literal table of kind x (gun, hydrant, mast, mast lit). The empty marker and other never stand.
- Aim: `AngleDeg`, and `AimFromTangent` on hand values that cover:
  - inward by winding
  - half the fall, half the lean
  - the 25 deg clamp
  - a NaN fall
  - a zero tangent

## PlacerTests

`PlacerTests` checks these items:

- Spacing is independent of node count, start node and winding.
- Closed ring coverage with the seam.
- Degenerate rings.
- One-sided straight, bent and hairpin ribbons, with the side checked per prop.
- The two-farthest-corners fallback.
- Class rules by kind and lit.
- Mast positions and aim.
- `MastClearM`.
- The mix over 30000 slots.
- A frozen `Pick` sequence from an independent plain-JS run of the hash.
- `RingKey`.
- `Diff`.
- Billing: `TrackGunsCanRun`, `TrackBilledHa` with the guns On and Off, `GunsOn`, and `GunWhyOf` for a track and for a track set to Off.
