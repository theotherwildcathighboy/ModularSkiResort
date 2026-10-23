# RunPropDensityRules: how the game picks area-corner props, the exact thinning formulas, and mast aiming

Decompile baseline `Game.dll` 1.6.0f1 (the 1.6.2f1 diff changes only `SecondaryObjectSystem.cs` :3499 and :3617, no shift, so these
lines hold for both).

## How the game fills one area corner

`RunPropDensityRules` mirrors the group branch of `CreateSecondaryObject` for one area corner (`Game.Objects/SecondaryObjectSystem.cs`):

- `CreateSecondaryAreaObjects` passes the run prefab entry's `Game.Prefabs.SubObject.m_Probability` on as the chance (:3004);
  `AreaInitializeSystem` sets it to 100 once, when the prefab entity is created (`Game.Prefabs/AreaInitializeSystem.cs:398`, fill
  :384-404; unchanged in 1.6.2).
- A group with `m_RandomizeGroupIndex` draws a fresh group index per corner (:3075-3077). Each member: the stream is copied (:3243)
  and advanced twice (:3244-3245); the first member takes the slot (:3251-3259); every later one, all tied at score 0 (no traffic
  data on a run), takes it when `random.NextInt(running weight sum) < its weight` (:3260-3271), the weight being the member's
  `SpawnableObjectData.m_Probability` read live (:3258, :3262). A zero sum returns (:3273-3276). Then the roll
  `random3.NextInt(100) < chance` on the PICKED member's copy (:3279), and the object's seed is the next draw on that copy
  (:3392-3393).

So a corner shows member X with probability chance/100 x w_X / W' (W' = the group's current weight sum): the pick is
weight-proportional and the roll is independent of it.

## Formulas

g, m = the clamped sliders; round(a / b) = (2a + b) / (2b) in long, half up, a >= 0, b > 0.

- `SnowmakingChance` = round(base x g / 100), 0..100. The snowmaking group's weights stay as they are while g > 0, so guns and
  hydrants show on chance x 75 % of corners (110 + 40 of 200): pure thinning, corner by corner.
- `NightChance` = round(base x (g x NM + m x M) / (100 x (NM + M))), 0..100; 0 when NM + M <= 0 or g = m = 0. NM = base weight of the
  night group's SHARED members (guns, hydrants, empty marker: also in the snowmaking group), M = base weight of its OWN members
  (the masts).
- `MastWeight` = 0 when base <= 0 or m = 0; base when g = 0; else max(1, round(base x m / g)).
- `NonMastWeight` = 0 when g = 0, else base.

Why exact: shared weights kept and masts scaled by r = m / g give W' = NM + rM, so chance g (NM + rM) / (NM + M) shows each shared
member on g x w_X / (NM + M) and each mast on m x w_X / (NM + M): today's share times its slider. At g = 0 the shared weights go
to 0 (snowmaking runs then have chance 0, so shared members show nowhere) and the chance is m x M / (NM + M). With m = g: r = 1,
chance g, pure thinning. Integer rounding keeps every tabled setting within 0.3 pp of the target. With g != m night corners re-pick
(same shares, other corners); the same settings give the same layout for the same group member order (the prefab-init order,
`Game.Prefabs/ObjectInitializeSystem.cs:900-905`).

`NextStable` / `ApplyDue`: the settle rule (an apply once the pair has been still for `SettleFrames` updates, or forced at a game
load). `ExpectedTenths` / `ClassOf`: the census log line only; the math above uses group membership, never names.

## Near MastAimRules

The game places an area-border prop with local +Z along the outgoing border segment (`Game.Objects/SecondaryObjectSystem.cs:2984-2992`,
`quaternion.LookRotationSafe`) and never consults the winding (`AreaFlags.CounterClockwise`), so the lamps (prefab `EffectSource`
rotation, pitch 25 deg about X, yaw 0 = +Z) shine ALONG the border and a prefab yaw cannot fix it: the two sides of a run mirror. We
turn the placed mast ourselves (`SkiNightLightSystem.AimMasts`) to look along the vector `MastAimRules` returns.

Rules:

- Interior side from the shoelace sign (CCW in x,z = interior on the LEFT of travel, the glade planner's convention):
  inward(d) = ccw ? (-d.z, d.x) : (d.z, -d.x).
- At a corner the two edges' inward normals are summed (the miter direction); a cusp, sum shorter than `CuspMinLen`, falls back to
  the outgoing edge's normal.
- Downhill bias: s = mean of the two edges' fall rate along travel (positive = traveling along the border loses height); the aim is
  inward + clamp(s / `SlopeFull`, -1, 1) x tan(`BiasMaxDeg`) x tangent, normalized. On a border along the fall line the lamps lean up
  to `BiasMaxDeg` toward downhill; across a contour line they aim straight in.
- The 25 deg downward pitch stays the prefab's (`EffectSource` rotation x 0.21644, w 0.976296 on all three masts).
- Deterministic: a function of the node ring only.
