// Pure arithmetic of the mountain jobs. The hut, summit lodge and both patrol buildings are roadless, so a Workplace on them would set
// RequireRoad (BuildingInitializeSystem.cs:229-283). The Welcome Sign they link to (SchoolLinkRules, 5 km) carries their jobs (SkiCatalog
// LodgeDef / PatrolDef .SignJobs). WorkProvider.m_MaxWorkers is the sum of SignJobs over every live linked building (cf.
// CityUtils.cs:41-78). Staff work at the sign and "ride up".
// Efficiency: WorkProviderSystem writes the staffing factors into the sign's Efficiency buffer (WorkProviderSystem.cs:252-281).
// With a mountain lodge in the area's tier-weighted mean, the staffing product moves to each lodge (MountainEfficiency) and the sign term
// excludes it (SignTerm), so a shortfall counts once. With none (patrol huts only, weight 0), it stays on the sign term.
// The carrier rule: while the area has an active road lodge, the nearest one to the sign carries these jobs instead of the sign. It also
// carries the own jobs of any off-road lodge on a cat track chain (LodgeSupplyRule.JobsLeave). With none, the sign carries them, so a chain
// that touches only a depot keeps its staff on the sign. A carrier lodge's own staffing factors then cover its own and the carried jobs.
// Pure file: System and System.Collections.Generic only (+ EntityRef). Linked into tests/BuildingBatch.

using System;
using System.Collections.Generic;

namespace SkiResortMod.Systems
{
    public static class MountainJobsRules
    {
        /// <summary>Per-sign targets: output[sign] += jobs[i] for each i with a linked sign, live[i] and jobs[i] &gt; 0. Signs with no
        /// contribution are absent. <paramref name="output"/> is cleared first. Lists of unequal length use the shortest.</summary>
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

        /// <summary>A sign without a WorkProvider gets one only for a target &gt; 0. A sign with one is set whenever it differs, to 0 as
        /// well. WorkProviderSystem lays the staff off (RefreshFreeWorkplace, WorkProviderSystem.cs:199-230). Removing the component would
        /// orphan the Worker rows.</summary>
        public static bool NeedsWrite(bool hasProvider, int current, int target)
        {
            return hasProvider ? current != target : target > 0;
        }

        /// <summary>Vanilla's new-workplace grace (CityServiceWorkplaceInitializeSystem.cs:111, :119) applies when a sign goes from no
        /// workplaces to some.</summary>
        public static bool StartsGrace(bool hasProvider, int current, int target)
        {
            return target > 0 && (!hasProvider || current <= 0);
        }

        /// <summary>The sign's efficiency without its staffing factors when a mountain lodge carries them in the mean.</summary>
        public static float SignTerm(float signEfficiency, float signWithoutStaffing, bool mountainInMean)
        {
            return mountainInMean ? signWithoutStaffing : signEfficiency;
        }

        /// <summary>A mountain lodge's efficiency: its own (budget) efficiency x the sign's staffing product. Negative or non-finite
        /// reads as 0.</summary>
        public static float MountainEfficiency(float own, float staffing)
        {
            float p = own * staffing;
            return p > 0f && !float.IsInfinity(p) ? p : 0f;
        }

        /// <summary>The jobs one roadless building puts on its sign. An Inactive building puts none: vanilla bills an Inactive top-level
        /// building no wages (CityServiceUpkeepSystem.GetUpkeepOfEmployeeWage :630-633), so a closed building must not keep paying staff
        /// through the sign. A lodge's jobs halve off-season (OffSeasonRules.HalfJobs, <paramref name="offSeasonHalf"/>). Patrol rows
        /// (<paramref name="lodge"/> false) stay full.</summary>
        public static int SignJobsFor(int signJobs, bool inactive, bool lodge, bool offSeasonHalf)
        {
            if (signJobs <= 0 || inactive)
                return 0;
            return lodge && offSeasonHalf ? OffSeasonRules.HalfJobs(signJobs) : signJobs;
        }

        /// <summary>The carrier pick: <paramref name="candidate"/> beats the best so far when it is nearer to the sign. A tie goes to the
        /// lower entity, so the pick does not move with chunk order.</summary>
        public static bool BetterCarrier(float distance, EntityRef candidate, float bestDistance, EntityRef best)
        {
            if (best.IsNull)
                return true;
            if (distance != bestDistance)
                return distance < bestDistance;
            return candidate.CompareTo(best) < 0;
        }

        /// <summary>A lodge's m_MaxWorkers: its own target (OffSeasonRules.Target, 0 when its jobs left) plus what it carries. OffSeasonStaffSystem
        /// is the one writer for lodges, so the re-assert on every poll keeps the sum.</summary>
        public static int CarrierTarget(int own, int carried)
        {
            return Math.Max(0, own) + Math.Max(0, carried);
        }

        /// <summary>An off-road lodge whose jobs left drops its WorkProvider only at 0 workplaces with no staff listed. Until then
        /// WorkProviderSystem's tick is still laying them off (RefreshFreeWorkplace, WorkProviderSystem.cs:199-230). Removing it earlier
        /// would orphan the Worker rows.</summary>
        public static bool CanDropProvider(bool jobsLeft, int maxWorkers, int staff)
        {
            return jobsLeft && maxWorkers <= 0 && staff == 0;
        }

        /// <summary>The sign carries jobs once this load's split is known (<paramref name="ready"/>) and its target &gt; 0 (no active road
        /// lodge in the area and at least one switched-on carried building). Before that, it carries the workplaces the save holds (a
        /// WorkProvider with m_MaxWorkers &gt; 0).</summary>
        public static bool SignCarriesJobs(bool ready, int target, bool hasProvider, int maxWorkers)
        {
            return ready ? target > 0 : hasProvider && maxWorkers > 0;
        }

        /// <summary>The hub's SignNoRoad row: the sign has no road edge AND carries jobs. Their staff are the only people who travel to the
        /// sign, so without jobs its road changes nothing.</summary>
        public static bool SignNoRoadRow(bool noRoad, bool carriesJobs)
        {
            return noRoad && carriesJobs;
        }

        /// <summary>Log word list of a sign's carried jobs, e.g. "summit 8 + patrol hut 2".</summary>
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
