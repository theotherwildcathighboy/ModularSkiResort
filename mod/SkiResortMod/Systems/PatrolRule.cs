// Ski patrol's two levers. A member run is PATROLLED when its top (RunFacts.TopNode, xz) lies within RadiusM (1 km, inclusive) of an ACTIVE
// patrol hut of its own ski area (hut and HQ prefabs share one row's numbers). Patrol lives in the score, not in SkiDayRules. A patrolled
// run gets these changes:
//   * Rated Black or Double Black: the type factor is multiplied by ExpertFactor (1.15).
//   * Not lift-served: the served factor is max(backcountry, BackcountryServedMin). A served run stays 1.
// Capacity follows through the served factor, which SkiResortScore.RunSkiersPerDay already multiplies. Patrol never gates night runs or
// powder.
// The file is pure and linked into tests/ResortBatch. It runs on the main thread (List). The consts alias SkiCatalog.Patrol.
// CatalogGuardTests reads RadiusM, ExpertFactor and BackcountryServedMin by name.

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    /// <summary>Ski patrol: expert runs within 1 km × 1.15, patrolled unserved runs served at ≥ 70 %.</summary>
    public static class PatrolRule
    {
        public const float RadiusM = SkiCatalog.Patrol.RadiusM;
        public const double ExpertFactor = SkiCatalog.Patrol.ExpertFactor;
        public const double BackcountryServedMin = SkiCatalog.Patrol.BackcountryServedMin;

        /// <summary>True when any finite entry of <paramref name="activePatrolXz"/> lies within RadiusM (inclusive, compared squared in
        /// float) of a finite <paramref name="runTopXz"/>. A null or empty list, or a non-finite run top, gives false.</summary>
        public static bool Patrolled(float2 runTopXz, List<float2> activePatrolXz)
        {
            if (activePatrolXz == null || activePatrolXz.Count == 0)
                return false;
            if (!math.all(math.isfinite(runTopXz)))
                return false;
            float r2 = RadiusM * RadiusM;
            for (int i = 0; i < activePatrolXz.Count; i++)
            {
                float2 p = activePatrolXz[i];
                if (!math.all(math.isfinite(p)))
                    continue;
                if (math.distancesq(runTopXz, p) <= r2)
                    return true;
            }
            return false;
        }

        /// <summary>Gives × ExpertFactor when <paramref name="patrolled"/> and the run is Rated Black or Double Black. Otherwise it gives
        /// <paramref name="typeFactor"/> unchanged (bit for bit).</summary>
        public static double TypeFactorOf(double typeFactor, bool patrolled, SkiResortScore.BandState state, byte band)
        {
            if (patrolled && state == SkiResortScore.BandState.Rated &&
                (band == SkiResortScore.BandBlack || band == SkiResortScore.BandDoubleBlack))
                return typeFactor * ExpertFactor;
            return typeFactor;
        }

        /// <summary>If served, the result is 1. If patrolled, it is max(backcountry, BackcountryServedMin). Otherwise it is backcountry. A
        /// NaN backcountry reads the minimum when patrolled.</summary>
        public static double ServedFactorOf(bool served, bool patrolled, double backcountry)
        {
            if (served)
                return 1.0;
            if (patrolled)
                return backcountry > BackcountryServedMin ? backcountry : BackcountryServedMin;
            return backcountry;
        }
    }
}
