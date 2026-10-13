// SchoolRule — the ski school's two levers (ROUND-SPEC R13; docs/research/art/assets-2026-10/code-round-2/
// round-2-contract.md §7.2, D13). Catalog Phase B, economy group, 2026-10-11.
//
//   * Score: +ScoreValue (12) to the area's terrain sum S, BEFORE × variety × vertical (SkiResortScore.Compute step 6),
//     while the ski school is ACTIVE (its InstalledUpgrade entry not Inactive) AND the area has a COUNTED green run.
//   * Demand: residents' ski-day chance × DemandFactor (1.2), outside the cap like W and P (SkiDayRules.Chance, D16);
//     tourists ignore it. Same condition (D13: one flag, one rule — "open green run" and "counted green run" differ only
//     for a green run worth 0, which teaches nobody).
// Never gates night runs or powder (R13).
//
// PURE FILE, Burst-callable: `using System;` only, and the two values are compile-time consts aliased from
// SkiCatalog.School (tests/ResortBatch CatalogGuardTests CG-1 reads ScoreValue and DemandFactor by name and compares them
// with the catalog). No SkiCatalog row is read here (contract risk R-a): SkiDaySystem passes the factor into its jobs as
// AreaJob.SchoolFactor, computed on the main thread from SkiResortScore.Result.SchoolActive / GreenCounted. Linked into
// tests/ResortBatch (LadderTests, SkiDayTests).

using System;

namespace SkiResortMod.Systems
{
    /// <summary>R13 ski school: +12 to S and locals × 1.2 while the school is active and a green run counts.</summary>
    public static class SchoolRule
    {
        /// <summary>Added to S (before variety and vertical) while active with a counted green run.</summary>
        public const double ScoreValue = SkiCatalog.School.ScoreValue;
        /// <summary>Residents' ski-day chance multiplier while active with a counted green run.</summary>
        public const double DemandFactor = SkiCatalog.School.DemandFactor;

        /// <summary>ScoreValue when the school is active and a green run counts, else 0.</summary>
        public static double ScoreValueOf(bool schoolActive, bool greenCounted)
        {
            return schoolActive && greenCounted ? ScoreValue : 0.0;
        }

        /// <summary>DemandFactor when the school is active and a green run counts, else 1 (no effect).</summary>
        public static double DemandFactorOf(bool schoolActive, bool greenCounted)
        {
            return schoolActive && greenCounted ? DemandFactor : 1.0;
        }
    }
}
