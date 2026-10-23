# SkiResortTourism: the game's city tourism formula and the per-resort figures derived from it

No per-building tourism exists (`TourismSystem` writes one city value), so the per-resort figures are derived from the game's formula
(Game.Simulation/TourismSystem.cs, 1.6.0f1; the same lines in 1.6.2f1):

- `:241`      providers = every AttractivenessProvider None {Temp, Deleted}
- `:101`      `s += (float)(a*a) / 10000f` (a float accumulator)
- `:104`      `s = 200f / (1f + math.exp(-0.3f * s)) - 100f`
- `:106`      `CityUtils.ApplyModifier(ref s, city modifiers, CityModifierType.Attractiveness)`
- `:107`      `Tourism.m_Attractiveness = Mathf.RoundToInt(s)`
- `:171-180`  `GetTargetTourists(a) = a <= 100 ? a*15 : 1500 + Mathf.RoundToInt(100 * Mathf.Log10(1 + (a - 100)))`
- `:158`      TourismSystem runs every 32768 frames, so the stored city value can be that old.

## Per-resort figures

- share = `100 * S_resort / S_all` with `S = sum a^2/10^4` (exact on the game's own sum).
- estimate = city value with every provider minus city value without the resort's providers, both from the same provider walk (the
  stored Tourism value can be 32768 frames older than the walk).

## Fidelity

`Term` is double while the game sums floats; `Logistic` casts the sum to float and evaluates the game's float expression of `:104`, so
only the float rounding of the sum differs (well below one attractiveness point for a realistic city). The game runs `:104` in a
`[BurstCompile]` job (TourismSystem.cs:21-22); Burst's exp may differ from managed exp in the last ulp, irrelevant after RoundToInt
except at a .5 tie. RoundToInt is `(int)Math.Round((double)f)`, banker's rounding, same as `UnityEngine.Mathf.RoundToInt`; the modifier
is applied before rounding (`:104-107`). `targetTourists` is passed in (the system passes `TourismSystem.GetTargetTourists`, `:171-180`)
so the file needs no Game types.
