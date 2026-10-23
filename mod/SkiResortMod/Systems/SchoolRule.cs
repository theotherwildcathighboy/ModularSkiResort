// Ski school levers, applied while the school is active and the area has a counted green run:
//   * Score: +ScoreValue added to the terrain sum S, before the variety and vertical multipliers.
//   * Demand: residents' ski-day chance x DemandFactor, outside the cap; tourists ignore it.
// Never gates night runs or powder.
// Pure and Burst-callable (`using System;` only). The constants alias SkiCatalog.School; CatalogGuardTests compares them
// by name. SkiDaySystem passes the factor into its jobs, computed on the main thread.

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
