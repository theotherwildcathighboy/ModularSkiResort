// The game's city tourism formula as pure code. The hub uses it to show a resort's share of the city's tourist draw and an
// estimated effect on city attractiveness and target tourists. No per-building tourism exists (TourismSystem writes one city
// value). So the per-resort figures come from the game's formula (Game.Simulation/TourismSystem.cs:101-107, 1.6.0f1, same in 1.6.2f1).
// Term is double, while the game sums floats. Logistic casts to float. So only the float rounding of the sum differs.
// Pure file: it uses System and Unity.Mathematics only (tests/ResortBatch links it). It does not allocate.
// Engine notes: EngineNotes/SkiResortTourism.md

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
        /// Logistic(0) = 0. It tends to 100 as s grows.</summary>
        public static float Logistic(double s)
        {
            float sf = (float)s;
            return 200f / (1f + math.exp(-0.3f * sf)) - 100f;
        }

        /// <summary>100 · sResort / sAll. The result is 0 when sAll &lt;= 0 (or either is not finite).</summary>
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
        /// (sAll - sResort, floored at 0). A null cityModifier is the identity. A null targetTourists gives 0 tourists.</summary>
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
