// PatrolRule — the ski patrol's two levers (ROUND-SPEC R13; docs/research/art/assets-2026-10/code-round-2/
// round-2-contract.md §7.2, D12). Catalog Phase B, economy group, 2026-10-11.
//
// A member run is PATROLLED when its top (the served rule's test point, SkiRunCatalogSystem RunFacts.TopNode, xz) lies
// within RadiusM (1 km, inclusive) of an ACTIVE patrol hut of its own ski area (either prefab: the hut and the HQ share
// one row's numbers, civic-sign D11). Patrol lives in the SCORE, not in SkiDayRules (D12): a patrolled run
//   * Rated Black or Double Black: its type factor × ExpertFactor (1.15);
//   * not lift-served: its served factor max(backcountry, BackcountryServedMin) = at least 70 % instead of the
//     backcountry setting alone; a served run stays 1 (served ignores patrol).
// Ski-day capacity follows through the served factor SkiResortScore.RunSkiersPerDay already multiplies. Never gates night
// runs or powder (R13): no other factor reads the flag.
//
// PURE FILE: `using System;`, `using System.Collections.Generic;`, `using Unity.Mathematics;` only, plus SkiCatalog
// consts and SkiResortScore's BandState / band literals (both linked wherever this file is). Main thread (List); the
// consts are compile-time aliases of SkiCatalog.Patrol (tests/ResortBatch CatalogGuardTests CG-1 reads RadiusM,
// ExpertFactor and BackcountryServedMin by name). Linked into tests/ResortBatch (LadderTests, SkiDayTests).

using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    /// <summary>R13 ski patrol: expert runs within 1 km × 1.15, patrolled unserved runs served at ≥ 70 %.</summary>
    public static class PatrolRule
    {
        public const float RadiusM = SkiCatalog.Patrol.RadiusM;
        public const double ExpertFactor = SkiCatalog.Patrol.ExpertFactor;
        public const double BackcountryServedMin = SkiCatalog.Patrol.BackcountryServedMin;

        /// <summary>True when any finite entry of <paramref name="activePatrolXz"/> lies within RadiusM (inclusive,
        /// compared squared in float: exact for the integer test distances) of a finite <paramref name="runTopXz"/>.
        /// Null / empty list or a non-finite run top (no facts yet) → false.</summary>
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

        /// <summary>× ExpertFactor when <paramref name="patrolled"/> and the run is Rated Black or Double Black; else
        /// <paramref name="typeFactor"/> unchanged (bit for bit: an unpatrolled run's part is today's).</summary>
        public static double TypeFactorOf(double typeFactor, bool patrolled, SkiResortScore.BandState state, byte band)
        {
            if (patrolled && state == SkiResortScore.BandState.Rated &&
                (band == SkiResortScore.BandBlack || band == SkiResortScore.BandDoubleBlack))
                return typeFactor * ExpertFactor;
            return typeFactor;
        }

        /// <summary>served → 1; else patrolled → max(backcountry, BackcountryServedMin); else backcountry (today's
        /// rule, bit for bit). A NaN backcountry (never: Compute passes the clamped percent / 100) reads the minimum when
        /// patrolled.</summary>
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
