// The game's city tourism formula, pure, so the hub can show a resort's share of the city's tourist draw and an estimated effect
// on city attractiveness and target tourists. No per-building tourism exists (TourismSystem writes one city value), so the
// per-resort figures are derived from the game's formula (Game.Simulation/TourismSystem.cs, 1.6.0f1; the same lines in 1.6.2f1):
//   :241      providers = every AttractivenessProvider None {Temp, Deleted}
//   :101      s += (float)(a·a) / 10000f            (a float accumulator)
//   :104      s = 200f / (1f + math.exp(-0.3f · s)) − 100f
//   :106      CityUtils.ApplyModifier(ref s, city modifiers, CityModifierType.Attractiveness)
//   :107      Tourism.m_Attractiveness = Mathf.RoundToInt(s)
//   :171-180  GetTargetTourists(a) = a <= 100 ? a·15 : 1500 + Mathf.RoundToInt(100 · Mathf.Log10(1 + (a − 100)))
//   :158      TourismSystem runs every 32768 frames, so the stored city value can be that old.
// Per-resort figures:
//   * share    = 100 * S_resort / S_all with S = sum a^2/10^4 (exact on the game's own sum)
//   * estimate = city value with every provider minus city value without the resort's providers, both from the same provider walk
//     (the stored Tourism value can be 32768 frames older than the walk)
// Fidelity: Term is double while the game sums floats; Logistic casts the sum to float and evaluates the game's float expression
// of :104, so only the float rounding of the sum differs (well below one attractiveness point for a realistic city). The game runs
// :104 in a [BurstCompile] job (TourismSystem.cs:21-22); Burst's exp may differ from managed exp in the last ulp, irrelevant after
// RoundToInt except at a .5 tie. RoundToInt is (int)Math.Round((double)f), banker's rounding, same as UnityEngine.Mathf.RoundToInt;
// the modifier is applied before rounding (:104-107). targetTourists is passed in (the system passes
// TourismSystem.GetTargetTourists, :171-180) so this file needs no Game types.
// Pure file: System and Unity.Mathematics only (tests/ResortBatch links it); no allocation.

using System;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public static class SkiResortTourism
    {
        /// <summary>One provider's term of the game's sum: a² / 10⁴ (TourismSystem.cs:101).</summary>
        public static double Term(int attractiveness)
        {
            return (double)attractiveness * attractiveness / 10000.0;
        }

        /// <summary>200 / (1 + exp(−0.3 s)) − 100 in the game's float arithmetic (TourismSystem.cs:104).
        /// Logistic(0) = 0; it tends to 100 as s grows.</summary>
        public static float Logistic(double s)
        {
            float sf = (float)s;
            return 200f / (1f + math.exp(-0.3f * sf)) - 100f;
        }

        /// <summary>100 · sResort / sAll; 0 when sAll &lt;= 0 (or either is not finite).</summary>
        public static float SharePct(double sResort, double sAll)
        {
            if (!(sAll > 0.0) || double.IsInfinity(sAll) || double.IsNaN(sResort) || double.IsInfinity(sResort))
                return 0f;
            return (float)(100.0 * sResort / sAll);
        }

        /// <summary>Same as Mathf.RoundToInt: (int)Math.Round((double)f), ties to even.</summary>
        public static int RoundToInt(float f)
        {
            return (int)Math.Round((double)f);
        }

        /// <summary>City attractiveness and target tourists with every provider (sAll) and without the resort's providers
        /// (sAll - sResort, floored at 0). A null cityModifier is the identity; a null targetTourists gives 0 tourists.</summary>
        public static void Estimate(double sAll, double sResort, Func<float, float> cityModifier,
            Func<int, int> targetTourists, out int attractivenessNow, out int attractivenessWithout,
            out int targetNow, out int targetWithout)
        {
            double without = sAll - sResort;
            if (!(without > 0.0))
                without = 0.0;
            float now = Logistic(sAll);
            float less = Logistic(without);
            if (cityModifier != null)
            {
                now = cityModifier(now);
                less = cityModifier(less);
            }
            attractivenessNow = RoundToInt(now);
            attractivenessWithout = RoundToInt(less);
            targetNow = targetTourists != null ? targetTourists(attractivenessNow) : 0;
            targetWithout = targetTourists != null ? targetTourists(attractivenessWithout) : 0;
        }
    }
}
