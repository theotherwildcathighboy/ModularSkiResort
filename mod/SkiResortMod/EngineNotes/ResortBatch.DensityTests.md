# DensityTests: the Options prop density rules and the engine draw-order mirror they are checked against

`DensityTests` covers `RunPropDensityRules`. Oracles are literal hand values (formulas evaluated by hand and with node), never a second
run of the same code. Not testable headless: the ECS writes, the `ChangedDistrict` re-stamp and the census.

## Sections

- D1 constants: PctMin 0, PctMax 100, PctStep 5, PctDefault 100, SettleFrames 30; ClassOther..ClassEmpty = 0..4.
- D2 `ClampPct`.
- D3 `SnowmakingChance(base, g)`: round(base x g / 100), half up (7 x 50 % = 3.5 -> 4).
- D4 `NightChance` (base 100; NM 200 shared = guns 110 + hydrants 40 + empty marker 50, M 150 masts; g, m), the no-mast and no-shared
  groups, and the empty group.
- D5 `MastWeight(base, g, m)` for the three masts 100 / 28 / 22 (the 5 % row rounds 1.4 and 1.1 up to the floor 1) and
  `NonMastWeight(base, g)`. At (100, 100) every written value equals its base: the default look is unchanged.
- D6 `NextStable` and `ApplyDue`: the settle rule (one apply per settled change; forced at a load; -1 = nothing applied).
- D7 `ExpectedTenths` (the census expectation) and `ClassOf` (census classes by name).
- D9 mast aim (`MastAimRules`).

## D8: a property test against a mirror of the engine's draw order

The mirror is test-only and reproduces the draw order for one area corner in `Game.Objects/SecondaryObjectSystem.cs`, group branch of
`CreateSecondaryObject`, 1.6.0f1 :3075-3286. The 1.6.2f1 diff changes only :3499 and :3617, so the range does not shift. It uses
`Unity.Mathematics.Random` from UnityMathematics.NoDeps 1.3.2 (the toolchain's com.unity.mathematics@1.3.2, `ResortBatch.csproj`).

- H1: thinning is monotone (a lower chance keeps a subset of the corners, same pick and seed).
- H2: the main stream does not depend on the weights.
- H3: the night shares match the formula in both member orders (20,000 corners: sd about 0.35 pp, tolerance +-1.0 pp; checked beforehand
  with a JavaScript port of the xorshift32 generator: all 24 pass, the largest gap 0.905 pp on "masts first" (100, 100) masts, 43.805 %
  against 42.9).
- H4: one state step per `NextInt` whatever the bound, with `NextInt(1)` in place of `NextInt(0)` (see the mirror's comment).
