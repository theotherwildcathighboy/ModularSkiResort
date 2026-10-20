// MountainJobsRules — the PURE arithmetic of the mountain jobs (Richard's ruling DECISIONS 347 (c), 2026-10-20: "Summit lodge no
// road for now, but it should add jobs via simulation like the other buildings"; applied to all four roadless buildings). Build
// note docs/research/field/2026-10-20/sim-round-build.md. Created 2026-10-20.
//
// THE RULE. The hut, the summit lodge and both patrol buildings are top-level and roadless since T113 (DECISIONS 344): any
// demand, workers included, would set RequireRoad on them (BuildingInitializeSystem.cs:229-283), so their prefabs carry no
// Workplace. Their jobs (SkiCatalog LodgeDef / PatrolDef .SignJobs: hut 4, summit 8, patrol HQ 2, patrol hut 2) are carried by
// the Welcome Sign they link to by distance (SchoolLinkRules, 5 km): the sign's WorkProvider.m_MaxWorkers = the sum of SignJobs
// over every LIVE top-level building linked to it, the number the ServiceUpgrade combine gave the sign before T113
// (CityUtils.GetCityServiceWorkplaceMaxWorkers, CityUtils.cs:41-78: owner + each installed upgrade's WorkplaceData; vanilla
// counted Inactive upgrades too, so does this rule). Staff work at the sign (it has the road) and "ride up".
//
// EFFICIENCY (D3 slice, DECISIONS 309 / 344): the sign's staffing factors (WorkProviderSystem writes NotEnoughEmployees,
// SickEmployees and EmployeeHappiness into the sign's Efficiency buffer, WorkProviderSystem.cs:252-281) now describe the mountain
// slices. When the area's tier-weighted mean holds at least one mountain lodge, the staffing product moves from the sign term to
// each mountain lodge's efficiency (MountainEfficiency) and the sign term is the sign WITHOUT those factors (SignTerm), so the
// shortfall is counted once. With no mountain lodge in the mean (patrol huts only: weight 0) it stays on the sign term, as
// before T113.
//
// PURE FILE: System and System.Collections.Generic only (+ EntityRef). Linked into tests/BuildingBatch (J1-J8).

using System;
using System.Collections.Generic;

namespace SkiResortMod.Systems
{
    public static class MountainJobsRules
    {
        /// <summary>The per-sign targets: for each i with a linked sign (<paramref name="signs"/>[i] not Null), <paramref name="live"/>[i]
        /// and <paramref name="jobs"/>[i] &gt; 0, output[sign] += jobs[i]. Signs with no contribution are absent (target 0).
        /// <paramref name="output"/> is cleared first. Lists of unequal length use the shortest.</summary>
        public static void SumBySign(IReadOnlyList<EntityRef> signs, IReadOnlyList<int> jobs, IReadOnlyList<bool> live,
                                     Dictionary<EntityRef, int> output)
        {
            output.Clear();
            int n = Math.Min(signs.Count, Math.Min(jobs.Count, live.Count));
            for (int i = 0; i < n; i++)
            {
                if (signs[i].IsNull || !live[i] || jobs[i] <= 0)
                    continue;
                output.TryGetValue(signs[i], out int sum);
                output[signs[i]] = sum + jobs[i];
            }
        }

        /// <summary>The write rule: a sign without a WorkProvider gets one only for a target &gt; 0; a sign with one is set whenever it
        /// differs (to 0 as well: the component is kept and WorkProviderSystem lays the staff off, RefreshFreeWorkplace,
        /// WorkProviderSystem.cs:199-230; removing it would orphan the Worker rows).</summary>
        public static bool NeedsWrite(bool hasProvider, int current, int target)
        {
            return hasProvider ? current != target : target > 0;
        }

        /// <summary>Vanilla's new-workplace grace (CityServiceWorkplaceInitializeSystem.cs:111, :119: m_EfficiencyCooldown =
        /// -m_ServiceBuildingEfficiencyGracePeriod) applies when a sign goes from no workplaces to some.</summary>
        public static bool StartsGrace(bool hasProvider, int current, int target)
        {
            return target > 0 && (!hasProvider || current <= 0);
        }

        /// <summary>The sign term of D3: without its staffing factors when a mountain lodge carries them in the mean.</summary>
        public static float SignTerm(float signEfficiency, float signWithoutStaffing, bool mountainInMean)
        {
            return mountainInMean ? signWithoutStaffing : signEfficiency;
        }

        /// <summary>A mountain lodge's D3 efficiency: its own (budget) efficiency x the sign's staffing product. Negative or
        /// non-finite reads as 0.</summary>
        public static float MountainEfficiency(float own, float staffing)
        {
            float p = own * staffing;
            return p > 0f && !float.IsInfinity(p) ? p : 0f;
        }

        /// <summary>The one log word list of a sign's carried jobs, e.g. "summit 8 + patrol hut 2".</summary>
        public static string Breakdown(IReadOnlyList<string> names, IReadOnlyList<int> jobs)
        {
            if (names.Count == 0)
                return "none linked";
            var parts = new List<string>(names.Count);
            for (int i = 0; i < names.Count && i < jobs.Count; i++)
                parts.Add(names[i] + " " + jobs[i]);
            return string.Join(" + ", parts);
        }
    }
}
