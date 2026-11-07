// The loop cache's cable-lane harvest. BuildLoopCache step 2 calls HarvestCableLanes. Both paths sort by lane entity (LiftLaneHarvest ORDER).
//   SCOPED (normal): the census's line edges + end nodes (LiftCensusSystem.CopyLineOwners), their SubLane buffers, the full scan's filter
//     lane by lane (LiftLaneHarvest.Scoped, Loop movers). O(lift edges). The census builds at Modification4B before this system (Mod.cs
//     anchor) and rebuilds the same frame on a lift edge Created / Deleted. Updated-only edits do not change the edge set (curves are read live).
//   FULL (fallback): the city-wide utility-lane scan (m_LaneQuery): census not registered, not armed (Editor) or not built yet, or after a miss.
//   WATCHDOG: the first scoped harvest of each load also runs the full scan and compares (one "[cost] lane harvest" line per load). A lane
//     only the full scan kept gives a WARN naming up to 5 lanes and owners. This rebuild uses the full result, and the load stays on the full
//     scan. A lane only the scoped harvest kept (filters drifted) gives a WARN and the full result.

using System.Collections.Generic;
using Colossal.Entities;
using Game.Net;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;

namespace SkiResortMod.Systems
{
    public sealed partial class ChairPinSystem
    {
        private readonly List<Entity> m_HarvestOwners = new List<Entity>();
        private readonly HashSet<Entity> m_HarvestSeen = new HashSet<Entity>();
        private readonly List<Entity> m_HarvestMissed = new List<Entity>();
        private readonly List<Entity> m_HarvestExtra = new List<Entity>();
        private bool m_HarvestVerified;        // the watchdog compare ran this load
        private bool m_HarvestFullThisLoad;    // the watchdog found a difference: full scan for the rest of the load
        private string m_DiagHarvestMode = "-";
        private int m_DiagHarvestOwners, m_DiagHarvestVisited;
        private double m_DiagHarvestMs;

        /// <summary>Per load (MoverReset).</summary>
        private void HarvestReset()
        {
            m_HarvestVerified = false;
            m_HarvestFullThisLoad = false;
            m_DiagHarvestMode = "-";
            m_DiagHarvestOwners = m_DiagHarvestVisited = 0;
            m_DiagHarvestMs = 0.0;
        }

        private List<LiftLoopCache.CableLane> HarvestCableLanes()
        {
            IReadOnlyDictionary<Entity, LiftTypeDef> lineTypes = m_PrefabCatalog.LiftLineTypes;
            LiftCensusSystem census = World.GetExistingSystemManaged<LiftCensusSystem>();
            int owners = census != null && !m_HarvestFullThisLoad ? census.CopyLineOwners(m_HarvestOwners) : -1;
            double msPerTick = 1000.0 / System.Diagnostics.Stopwatch.Frequency;

            if (owners < 0)
            {
                var full = new List<LiftLoopCache.CableLane>();
                long f0 = System.Diagnostics.Stopwatch.GetTimestamp();
                FullLaneScan(lineTypes, full, out int seen, out int hv);
                double fullMs = (System.Diagnostics.Stopwatch.GetTimestamp() - f0) * msPerTick;
                m_DiagHarvestMode = census == null ? "full scan (census not registered)"
                    : m_HarvestFullThisLoad ? "full scan (the watchdog found a difference this load)"
                    : "full scan (census not armed)";
                m_DiagHarvestOwners = 0;
                m_DiagHarvestVisited = seen;
                m_DiagHarvestMs = fullMs;
                m_DiagLaneTotal = seen;
                if (!m_LaneScanLogged)
                {
                    m_LaneScanLogged = true;
                    Mod.Log.Info($"{nameof(ChairPinSystem)} [cost] lane harvest (first rebuild of the load): {m_DiagHarvestMode}: " +
                                 $"{seen} utility lane(s) in the city, {hv} of an HV lane prefab, {full.Count} kept as chairlift cable " +
                                 $"lane(s), {fullMs:F2} ms");
                }
                return full;
            }

            var scoped = new List<LiftLoopCache.CableLane>();
            long s0 = System.Diagnostics.Stopwatch.GetTimestamp();
            int visited = LiftLaneHarvest.Scoped(EntityManager, m_HarvestOwners, lineTypes, MoverKind.Loop, scoped, m_HarvestSeen);
            LiftLaneHarvest.SortByLane(scoped);
            double scopedMs = (System.Diagnostics.Stopwatch.GetTimestamp() - s0) * msPerTick;
            m_DiagHarvestMode = "scoped (census line edges + nodes)";
            m_DiagHarvestOwners = owners;
            m_DiagHarvestVisited = visited;
            m_DiagHarvestMs = scopedMs;
            if (m_HarvestVerified)
                return scoped;

            // The once-per-load watchdog: the city-wide scan next to the scoped harvest.
            m_HarvestVerified = true;
            var check = new List<LiftLoopCache.CableLane>();
            long w0 = System.Diagnostics.Stopwatch.GetTimestamp();
            FullLaneScan(lineTypes, check, out int citySeen, out int cityHv);
            double watchdogMs = (System.Diagnostics.Stopwatch.GetTimestamp() - w0) * msPerTick;
            m_DiagLaneTotal = citySeen;
            LiftLaneHarvest.Compare(check, scoped, m_HarvestMissed, m_HarvestExtra);
            bool same = m_HarvestMissed.Count == 0 && m_HarvestExtra.Count == 0;
            m_LaneScanLogged = true;
            Mod.Log.Info($"{nameof(ChairPinSystem)} [cost] lane harvest (first scoped rebuild of the load): scoped {scoped.Count} " +
                         $"chairlift cable lane(s) from {owners} line edge/node owner(s) ({visited} SubLane entries) in {scopedMs:F3} ms; " +
                         $"watchdog full scan {citySeen} utility lane(s) in the city, {cityHv} of an HV lane prefab, {check.Count} kept, " +
                         $"{watchdogMs:F2} ms — " +
                         (same ? "identical sets; the rest of this load harvests scoped"
                               : $"{m_HarvestMissed.Count} missed / {m_HarvestExtra.Count} extra; this rebuild and the rest of the " +
                                 "load use the full scan (WARN below)"));
            if (same)
                return scoped;

            m_HarvestFullThisLoad = true;
            m_DiagHarvestMode = "full scan (the watchdog found a difference this load)";
            if (m_HarvestMissed.Count > 0)
                Mod.Log.Warn($"{nameof(ChairPinSystem)} [harvest] the city-wide lane scan found {m_HarvestMissed.Count} chairlift cable " +
                             $"lane(s) the census-scoped harvest missed ({LiftLaneHarvest.Describe(EntityManager, m_HarvestMissed, 5)}) — " +
                             "using the full scan for the rest of this load; please report this line (R-4 watchdog)");
            if (m_HarvestExtra.Count > 0)
                Mod.Log.Warn($"{nameof(ChairPinSystem)} [harvest] the census-scoped harvest kept {m_HarvestExtra.Count} lane(s) the " +
                             $"city-wide scan did not ({LiftLaneHarvest.Describe(EntityManager, m_HarvestExtra, 5)}) — the two filters " +
                             "drifted; using the full scan for the rest of this load; please report this line (R-4 watchdog)");
            return check;
        }

        /// <summary>The city-wide scan (m_LaneQuery: every utility lane), sorted by lane. The lane PREFAB is tested first through
        /// the cached lookup (CableLanePrefabs), so only HV-lane-prefab lanes pay for the owner read.</summary>
        private void FullLaneScan(IReadOnlyDictionary<Entity, LiftTypeDef> lineTypes, List<LiftLoopCache.CableLane> lanes,
            out int lanesSeen, out int lanesHv)
        {
            lanesSeen = 0;
            lanesHv = 0;
            // Complete the writers of the query's components first (same pattern as TramShuttleSystem.HarvestLanes).
            m_LaneQuery.CompleteDependency();
            var chunks = m_LaneQuery.ToArchetypeChunkArray(Allocator.Temp);
            try
            {
                var entityHandle = GetEntityTypeHandle();
                var laneHandle = GetComponentTypeHandle<Game.Net.Lane>(isReadOnly: true);
                var curveHandle = GetComponentTypeHandle<Curve>(isReadOnly: true);
                var ownerHandle = GetComponentTypeHandle<Game.Common.Owner>(isReadOnly: true);
                var prefabRefHandle = GetComponentTypeHandle<PrefabRef>(isReadOnly: true);
                for (int c = 0; c < chunks.Length; c++)
                {
                    var entities = chunks[c].GetNativeArray(entityHandle);
                    var laneData = chunks[c].GetNativeArray(ref laneHandle);
                    var curves = chunks[c].GetNativeArray(ref curveHandle);
                    var owners = chunks[c].GetNativeArray(ref ownerHandle);
                    var prefabRefs = chunks[c].GetNativeArray(ref prefabRefHandle);
                    lanesSeen += entities.Length;
                    for (int i = 0; i < entities.Length; i++)
                    {
                        if (!CableLanePrefabs.IsHighVoltage(EntityManager, prefabRefs[i].m_Prefab))
                            continue;
                        lanesHv++;
                        Entity ownerEntity = owners[i].m_Owner;
                        if (ownerEntity == Entity.Null ||
                            !EntityManager.TryGetComponent<PrefabRef>(ownerEntity, out var ownerPrefab) ||
                            !lineTypes.TryGetValue(ownerPrefab.m_Prefab, out LiftTypeDef lineDef) || lineDef == null ||
                            lineDef.Mover != MoverKind.Loop)
                            continue;
                        lanes.Add(new LiftLoopCache.CableLane
                        {
                            m_Lane = entities[i],
                            m_Owner = ownerEntity,
                            m_Bezier = curves[i].m_Bezier,
                            m_Start = laneData[i].m_StartNode,
                            m_End = laneData[i].m_EndNode,
                            m_Length = curves[i].m_Length,
                            m_TypeIndex = lineDef.Index,
                        });
                    }
                }
            }
            finally
            {
                chunks.Dispose();
            }
            LiftLaneHarvest.SortByLane(lanes);
        }
    }
}
