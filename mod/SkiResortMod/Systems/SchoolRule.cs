// Ski school levers. They apply while the school is active and the area has a counted green run:
//   * Score: +ScoreValue goes to the terrain sum S, before the variety and vertical multipliers.
//   * Demand: residents' ski-day chance is multiplied by DemandFactor, outside the cap. Tourists ignore it.
// The school never gates night runs or powder.
// The file is pure and Burst-callable (`using System;` only). The constants alias SkiCatalog.School. CatalogGuardTests compares them by
// name.
// The main thread computes the factor, and SkiDaySystem passes it into its jobs.

using System;

namespace SkiResortMod.Systems
{
    /// <summary>Ski school: +12 to S and locals x 1.2 while the school is active and a green run counts.</summary>
    public static class SchoolRule
    {
        public const double ScoreValue = SkiCatalog.School.ScoreValue;
        public const double DemandFactor = SkiCatalog.School.DemandFactor;

        public static double ScoreValueOf(bool schoolActive, bool greenCounted)
        {
            return schoolActive && greenCounted ? ScoreValue : 0.0;
        }

        public static double DemandFactorOf(bool schoolActive, bool greenCounted)
        {
            return schoolActive && greenCounted ? DemandFactor : 1.0;
        }
    }
}
