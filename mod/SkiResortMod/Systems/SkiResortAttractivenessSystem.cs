// SkiResortAttractivenessSystem — painted ski terrain near a lodge produces tourism
// attraction (SPEC §6, Phase 2 code milestone). The golf-pack anchor model: the LODGE
// carries the numbers; trails and lifts near it feed the score.
//
// ─────────────────────────────────────────────────────────────────────────────────────────
// WRITE PATH (the critical engineering — re-verified against fullsrc this round)
//
// Vanilla Game.Simulation.AttractionSystem (interval 16, GameSimulation) unconditionally
// rewrites AttractivenessProvider for every building in its per-tick UpdateFrame slice:
//
//     query All { AttractivenessProvider RW, PrefabRef RO, UpdateFrame } None { Destroyed, Deleted, Temp }
//     ...
//     AttractionData data = default;                         // prefab-side input
//     if (m_AttractionDatas.HasComponent(prefab)) data = m_AttractionDatas[prefab];
//     ...
//     nativeArray2[i] = new AttractivenessProvider { m_Attractiveness = Mathf.RoundToInt(num) };
//
// Two viable anchors were on the table (code-mod.md §1.3):
//   (a) write the PREFAB's AttractionData (input side) — rejected: prefab data is shared
//       by every placed instance, so two lodges could never score differently;
//   (b) anchored overwrite — CHOSEN: this system is registered
//       UpdateAfter<..., AttractionSystem>(GameSimulation) with the same interval 16
//       (anchored systems inherit the anchor's offset — verified in Game.UpdateSystem),
//       so each of our writes lands in the same tick as vanilla's, immediately after,
//       and simply wins. We write ALL cached lodges every tick rather than mirroring the
//       slice bookkeeping: vanilla only touches the current slice, our post-write is
//       idempotent, and lodge counts are tiny — the code-mod.md fallback, promoted to
//       the design because it removes the shared-component bookkeeping entirely.
//
// The lodge prefab ships Attraction 20 (= TierBase), so instances carry
// AttractivenessProvider from their vanilla archetype; the AddComponent path below stays
// as a fallback for prefab variants without it (the Tourism Overhaul
// HistoricAttractivenessSystem precedent). Vanilla's slice pass writes the flat
// prefab-derived score; our anchored write lands immediately after in the same tick and
// wins. Deliberate redundancy: if this system is ever error-disabled, the lodge degrades
// to a flat attraction-20 building instead of dead weight. TourismSystem sums every
// provider (squared/10000) — the lodge feeds city attractiveness and tourist demand
// exactly like the golf clubhouse's flat 50.
//
// ─────────────────────────────────────────────────────────────────────────────────────────
// SCORE MODEL (v1 — every constant in the Tuning block below)
//
//   trailsTerm = min(AreaWeight × Σ_i sqrt(area_i)  +  DropWeight × maxDrop,  TrailsCap)
//   liftsTerm  = min(PerStationBonus × stationsInRadius,  LiftsCap)   // half-weight per
//                                                                     // station ≈ full per
//                                                                     // base+top pair
//   cov_i      = snowmaking && T_local(meanElev_i) ≤ freezing + Margin ? 1
//              : clamp01((topElev_i − snowLine) / vertical_i)          // natural cover
//   coverage   = Σ(cov_i · area_i) / Σ(area_i)                         // 0 with no trails
//   season     = max(coverage, SummerFloor)                            // deck season
//   score      = round((TierBase + trailsTerm + liftsTerm) × season)
//
// Trail geometry is read from data the game maintains: Game.Areas.Geometry.m_SurfaceArea
// (true triangulated area) and m_Bounds (terrain-conformed min/max elevation — the area
// pipeline drapes triangles onto terrain, trails-surfaces-glades.md §1). No terrain
// sampling needed. Snow math comes from SnowLineSystem's validated lapse formula.
// Radius: the golf pack's 1 km ServiceUpgrade tether is the shipped precedent.
//
// CADENCE: recompute only on relevant Created/Updated events (lodges/stations/our areas,
// prefab-filtered) plus a slow periodic refresh for seasonal drift (PeriodicRefreshTicks
// × 16-frame ticks = 4096 frames). The per-tick work outside recomputes is one cached
// int write per lodge. Never a per-frame scan.
//
// TOURISM OVERHAUL: presence re-checked at game load (the OnLoad-time scan raced mod
// load order — see Mod.RefreshTourismOverhaulDetection); if present we log intent only.
// Integration constants (crowding fold-in per code-mod.md addendum) are stubbed in
// Tuning for a later round.
// ─────────────────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;
using Colossal.Entities;
using Colossal.Serialization.Entities;
using Game;
using Game.Areas;
using Game.Buildings;
using Game.Common;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public sealed partial class SkiResortAttractivenessSystem : GameSystemBase
    {
        // ── Tuning — the single block (v1 numbers; tier array ready for future lodges) ──
        private static class Tuning
        {
            public const float LodgeRadiusMeters = 1000f;   // golf ServiceUpgrade tether precedent

            // Tier 1 base (modest vs the clubhouse's flat 50; future tiers append here).
            public const float TierOneBase = 20f;

            // Trails: sqrt(area) rewards breadth with diminishing returns; drop rewards
            // real vertical. Example: 3 × 20 000 m² trails + 350 m drop → 21 + 14 = 35.
            public const float TrailAreaWeight = 0.05f;     // × sqrt(m²), per surface
            public const float TrailDropWeight = 0.04f;     // × max vertical drop (m)
            public const float TrailsTermCap = 45f;

            // Lifts: per STATION half-weight (base+top pair ≈ one lift's full bonus) —
            // pairing stations geometrically is not worth the code at v1 scale.
            public const float PerStationBonus = 4f;
            public const float LiftsTermCap = 16f;

            // Season: snowmaking extends the season Margin °C above freezing (SPEC §6.6
            // extended-season mechanic); summer keeps the deck open at the floor.
            public const float SnowmakingMarginC = 2f;
            public const float SummerFloor = 0.25f;

            // Night skiing (night-skiing-run.md §4.4): NO time-of-day hook exists anywhere
            // in vanilla tourism/leisure desirability, so the bonus is FLAT — weighted by
            // the lit share of the resort's run area and implicitly gated on cover (the
            // night term accumulates cover × area, so an unlit-able warm run contributes
            // nothing: you only get the bonus while you are also paying the 800 rate).
            // TourismSystem SQUARES the provider, so +12 on a score of 40 is +69% to that
            // lodge's tourist draw.
            public const float NightBonus = 12f;

            // Seasonal drift refresh: 256 of our 16-frame ticks = 4096 frames.
            public const int PeriodicRefreshTicks = 256;

            // Tourism Overhaul integration (stubbed — intent-log only this round).
            public const float TouristCrowdingFoldIn = 0f;  // reserved (code-mod.md addendum)
        }

        // Prefab sets — all tolerant-resolution (not yet imported ⇒ system stays inert).
        private static readonly string[] LodgePrefabNames = { "SkiAFrameDayLodge" };
        private static readonly string[] GroomedSurfaceNames = { "SkiRunGroomedA" };
        private static readonly string[] SnowmakingSurfaceNames = { "SkiRunSnowmakingA" };
        private static readonly string[] NightSurfaceNames = { "SkiRunNightA" };

        private PrefabSystem m_PrefabSystem;
        private PowerBridgeSystem m_PowerBridgeSystem; // shared station prefab set
        private SnowLineSystem m_SnowLineSystem;       // validated climate oracle

        private readonly HashSet<Entity> m_LodgePrefabs = new HashSet<Entity>();
        private readonly HashSet<Entity> m_GroomedPrefabs = new HashSet<Entity>();
        private readonly HashSet<Entity> m_SnowmakingPrefabs = new HashSet<Entity>();
        private readonly HashSet<Entity> m_NightPrefabs = new HashSet<Entity>();

        /// <summary>Lodge entity → cached score, refreshed by Recompute, written every tick.</summary>
        private readonly Dictionary<Entity, int> m_Scores = new Dictionary<Entity, int>();

        private EntityQuery m_BuildingScanQuery;  // lodges + stations (prefab-filtered)
        private EntityQuery m_BuildingEventQuery;
        private EntityQuery m_AreaScanQuery;      // painted ski surfaces (prefab-filtered)
        private EntityQuery m_AreaEventQuery;
        private EntityQuery m_PrefabScanQuery;    // name-resolution fallback

        private bool m_PrefabsResolved;
        private bool m_RecomputePending;
        private int m_TicksUntilRefresh = Tuning.PeriodicRefreshTicks;
        private bool m_Disabled;
        private bool m_ErrorLogged;

        /// <summary>Mirror vanilla AttractionSystem's cadence so the anchored write lands
        /// right after vanilla's each tick (see the write-path header).</summary>
        public override int GetUpdateInterval(SystemUpdatePhase phase)
        {
            return 16;
        }

        protected override void OnCreate()
        {
            base.OnCreate();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_PowerBridgeSystem = World.GetOrCreateSystemManaged<PowerBridgeSystem>();
            m_SnowLineSystem = World.GetOrCreateSystemManaged<SnowLineSystem>();

            m_BuildingScanQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Building>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                    ComponentType.ReadOnly<Game.Objects.Transform>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Temp>(),
                    ComponentType.ReadOnly<Destroyed>(),
                },
            });
            m_BuildingEventQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Building>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                },
                Any = new[]
                {
                    ComponentType.ReadOnly<Created>(),
                    ComponentType.ReadOnly<Updated>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Temp>(),
                },
            });
            m_AreaScanQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Area>(),
                    ComponentType.ReadOnly<Geometry>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Temp>(),
                },
            });
            m_AreaEventQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Area>(),
                    ComponentType.ReadOnly<Geometry>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                },
                Any = new[]
                {
                    ComponentType.ReadOnly<Created>(),
                    ComponentType.ReadOnly<Updated>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Temp>(),
                },
            });
            m_PrefabScanQuery = GetEntityQuery(ComponentType.ReadOnly<PrefabData>());

            Mod.Log.Info($"{nameof(SkiResortAttractivenessSystem)} created");
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            if (mode != GameMode.Game && mode != GameMode.Editor)
                return;

            ResolvePrefabs();
            Mod.RefreshTourismOverhaulDetection(); // fixes the OnLoad-time load-order race
            if (Mod.TourismOverhaulDetected)
                Mod.Log.Info("Attractiveness: Tourism Overhaul present — coexisting via the idempotent " +
                             "16-frame rewrite; crowding fold-in reserved (Tuning.TouristCrowdingFoldIn)");

            m_Disabled = false;
            m_ErrorLogged = false;
            m_Scores.Clear();
            m_RecomputePending = true;
            m_TicksUntilRefresh = Tuning.PeriodicRefreshTicks;
        }

        protected override void OnUpdate()
        {
            try
            {
                if (m_Disabled || m_LodgePrefabs.Count == 0)
                    return; // lodge prefab not imported yet — inert by design

                // Event-driven recompute: any Created/Updated among OUR lodges/stations/
                // surfaces (prefab-filtered — a city full of unrelated building events
                // stays cheap: emptiness checks first, chunk scans only on event ticks).
                if (!m_RecomputePending && !m_BuildingEventQuery.IsEmptyIgnoreFilter &&
                    AnyMatchingPrefab(m_BuildingEventQuery, m_LodgePrefabs,
                        m_PowerBridgeSystem.GetResolvedStationPrefabs()))
                    m_RecomputePending = true;
                if (!m_RecomputePending && !m_AreaEventQuery.IsEmptyIgnoreFilter &&
                    AnyMatchingPrefab(m_AreaEventQuery, m_GroomedPrefabs, m_SnowmakingPrefabs,
                        m_NightPrefabs))
                    m_RecomputePending = true;

                // Periodic refresh: seasons move the snow line without touching entities.
                if (--m_TicksUntilRefresh <= 0)
                {
                    m_TicksUntilRefresh = Tuning.PeriodicRefreshTicks;
                    m_RecomputePending = true;
                }

                if (m_RecomputePending)
                {
                    m_RecomputePending = false;
                    Recompute();
                }

                WriteProviders();
            }
            catch (Exception e)
            {
                m_Disabled = true;
                if (!m_ErrorLogged)
                {
                    m_ErrorLogged = true;
                    Mod.Log.Error($"!!! {nameof(SkiResortAttractivenessSystem)} EXCEPTION — attractiveness " +
                                  $"disabled until the next game load. Please report this stack:\n{e}");
                }
            }
        }

        // ─────────────────────────────────────────────────────────────────────────────────
        // The recompute: gather our few entities, score each lodge, cache, log summaries.
        // ─────────────────────────────────────────────────────────────────────────────────

        private struct TrailInfo
        {
            public float3 m_Center;
            public float m_Area;      // Geometry.m_SurfaceArea (triangulated, true m²)
            public float m_BottomY;   // Geometry.m_Bounds min y (terrain-conformed)
            public float m_TopY;      // Geometry.m_Bounds max y
            public bool m_Snowmaking; // true for night runs too — they ARE snowmaking runs
            public bool m_Night;
        }

        private void Recompute()
        {
            var trails = new List<TrailInfo>();
            var stationPositions = new List<float3>();
            var lodges = new List<Entity>();

            HashSet<Entity> stationPrefabs = m_PowerBridgeSystem.GetResolvedStationPrefabs();

            // Areas: our painted surfaces.
            var areaChunks = m_AreaScanQuery.ToArchetypeChunkArray(Allocator.Temp);
            try
            {
                var prefabRefHandle = GetComponentTypeHandle<PrefabRef>(isReadOnly: true);
                var geometryHandle = GetComponentTypeHandle<Geometry>(isReadOnly: true);
                for (int c = 0; c < areaChunks.Length; c++)
                {
                    var prefabRefs = areaChunks[c].GetNativeArray(ref prefabRefHandle);
                    var geometries = areaChunks[c].GetNativeArray(ref geometryHandle);
                    for (int i = 0; i < prefabRefs.Length; i++)
                    {
                        // Night first; night runs count as snowmaking for the seasonal
                        // cover branch (same guns, same margin) and additionally as
                        // night for the flat bonus.
                        bool night = m_NightPrefabs.Contains(prefabRefs[i].m_Prefab);
                        bool snowmaking = night ||
                            m_SnowmakingPrefabs.Contains(prefabRefs[i].m_Prefab);
                        if (!snowmaking && !m_GroomedPrefabs.Contains(prefabRefs[i].m_Prefab))
                            continue;
                        Geometry geometry = geometries[i];
                        trails.Add(new TrailInfo
                        {
                            m_Center = geometry.m_CenterPosition,
                            m_Area = math.max(geometry.m_SurfaceArea, 0f),
                            m_BottomY = geometry.m_Bounds.min.y,
                            m_TopY = geometry.m_Bounds.max.y,
                            m_Snowmaking = snowmaking,
                            m_Night = night,
                        });
                    }
                }
            }
            finally
            {
                areaChunks.Dispose();
            }

            // Buildings: lodges + stations in one scan.
            var buildingChunks = m_BuildingScanQuery.ToArchetypeChunkArray(Allocator.Temp);
            try
            {
                var entityHandle = GetEntityTypeHandle();
                var prefabRefHandle = GetComponentTypeHandle<PrefabRef>(isReadOnly: true);
                var transformHandle = GetComponentTypeHandle<Game.Objects.Transform>(isReadOnly: true);
                for (int c = 0; c < buildingChunks.Length; c++)
                {
                    var entities = buildingChunks[c].GetNativeArray(entityHandle);
                    var prefabRefs = buildingChunks[c].GetNativeArray(ref prefabRefHandle);
                    var transforms = buildingChunks[c].GetNativeArray(ref transformHandle);
                    for (int i = 0; i < entities.Length; i++)
                    {
                        if (m_LodgePrefabs.Contains(prefabRefs[i].m_Prefab))
                            lodges.Add(entities[i]);
                        else if (stationPrefabs.Contains(prefabRefs[i].m_Prefab))
                            stationPositions.Add(transforms[i].m_Position);
                    }
                }
            }
            finally
            {
                buildingChunks.Dispose();
            }

            m_Scores.Clear();
            float snowLine = m_SnowLineSystem.GetSnowLineElevation();
            float freezing = m_SnowLineSystem.GetFreezingTemperature();

            foreach (Entity lodge in lodges)
            {
                float3 lodgePosition = EntityManager.GetComponentData<Game.Objects.Transform>(lodge).m_Position;
                float radiusSq = Tuning.LodgeRadiusMeters * Tuning.LodgeRadiusMeters;

                float sqrtAreaSum = 0f, maxDrop = 0f, areaSum = 0f, coveredAreaSum = 0f;
                float nightCoveredAreaSum = 0f;
                int trailCount = 0;
                foreach (TrailInfo trail in trails)
                {
                    if (math.distancesq(trail.m_Center.xz, lodgePosition.xz) > radiusSq)
                        continue;
                    trailCount++;
                    sqrtAreaSum += math.sqrt(trail.m_Area);
                    float vertical = math.max(trail.m_TopY - trail.m_BottomY, 0f);
                    maxDrop = math.max(maxDrop, vertical);

                    // Seasonal cover: snowmaking beats the lapse by the margin; natural
                    // cover is the fraction of the trail's vertical above the snow line.
                    float cover;
                    float meanElevation = (trail.m_TopY + trail.m_BottomY) * 0.5f;
                    if (trail.m_Snowmaking &&
                        m_SnowLineSystem.GetLocalTemperature(meanElevation) <= freezing + Tuning.SnowmakingMarginC)
                        cover = 1f;
                    else
                        cover = math.saturate((trail.m_TopY - snowLine) / math.max(vertical, 1f));
                    areaSum += trail.m_Area;
                    coveredAreaSum += cover * trail.m_Area;
                    if (trail.m_Night)
                        nightCoveredAreaSum += cover * trail.m_Area;
                }

                int stationCount = 0;
                foreach (float3 stationPosition in stationPositions)
                {
                    if (math.distancesq(stationPosition.xz, lodgePosition.xz) <= radiusSq)
                        stationCount++;
                }

                float trailsTerm = math.min(
                    Tuning.TrailAreaWeight * sqrtAreaSum + Tuning.TrailDropWeight * maxDrop,
                    Tuning.TrailsTermCap);
                float liftsTerm = math.min(Tuning.PerStationBonus * stationCount, Tuning.LiftsTermCap);
                float coverage = areaSum > 0f ? coveredAreaSum / areaSum : 0f;
                float season = math.max(coverage, Tuning.SummerFloor);
                // Night term: flat bonus weighted by the LIT share of run area, using
                // cover-weighted areas on both sides of the ratio — so a night run only
                // scores while its guns can actually hold snow (the same window it bills
                // the 800 rate for). No time-of-day factor exists to ride (§4.4).
                float nightTerm = Tuning.NightBonus *
                    (nightCoveredAreaSum / math.max(areaSum, 1f));
                int score = (int)math.round(
                    (Tuning.TierOneBase + trailsTerm + liftsTerm + nightTerm) * season);

                m_Scores[lodge] = score;
                Mod.Log.Info(
                    $"Attractiveness: lodge '{m_PrefabSystem.GetPrefabName(EntityManager.GetComponentData<PrefabRef>(lodge).m_Prefab)}' " +
                    $"= {score} ({trailCount} trail(s) {areaSum:F0} m², drop {maxDrop:F0} m, " +
                    $"{stationCount} station(s), snow coverage {coverage:P0}, " +
                    $"night term +{nightTerm:F1}, season ×{season:F2})");
            }
        }

        // ─────────────────────────────────────────────────────────────────────────────────
        // The anchored write: every tick (interval 16, right after vanilla AttractionSystem),
        // copy the cached score into AttractivenessProvider — adding the component on first
        // contact (the lodge prefab ships no Attraction component yet).
        // ─────────────────────────────────────────────────────────────────────────────────
        private void WriteProviders()
        {
            if (m_Scores.Count == 0)
                return;

            List<Entity> stale = null;
            foreach (var pair in m_Scores)
            {
                Entity lodge = pair.Key;
                if (!EntityManager.Exists(lodge) || EntityManager.HasComponent<Deleted>(lodge))
                {
                    (stale = stale ?? new List<Entity>()).Add(lodge);
                    continue;
                }

                if (!EntityManager.HasComponent<AttractivenessProvider>(lodge))
                {
                    // Instance now matches vanilla AttractionSystem's query; vanilla zeroes
                    // it per slice pass and our anchored write restores it the same tick.
                    EntityManager.AddComponentData(lodge, new AttractivenessProvider { m_Attractiveness = pair.Value });
                    Mod.Log.Info($"Attractiveness: AttractivenessProvider added to lodge (initial {pair.Value})");
                    continue;
                }

                EntityManager.SetComponentData(lodge, new AttractivenessProvider { m_Attractiveness = pair.Value });
            }

            if (stale != null)
            {
                foreach (Entity lodge in stale)
                    m_Scores.Remove(lodge);
            }
        }

        /// <summary>True when any entity in the (event) query's chunks has a PrefabRef in
        /// any of the given sets. Runs only on event ticks; chunk-level iteration.
        /// Widened from two fixed sets to params when the night run type arrived —
        /// the params array allocation is once per EVENT tick, not per frame.</summary>
        private bool AnyMatchingPrefab(EntityQuery query, params HashSet<Entity>[] sets)
        {
            var chunks = query.ToArchetypeChunkArray(Allocator.Temp);
            try
            {
                var prefabRefHandle = GetComponentTypeHandle<PrefabRef>(isReadOnly: true);
                for (int c = 0; c < chunks.Length; c++)
                {
                    var prefabRefs = chunks[c].GetNativeArray(ref prefabRefHandle);
                    for (int i = 0; i < prefabRefs.Length; i++)
                    {
                        Entity prefab = prefabRefs[i].m_Prefab;
                        for (int s = 0; s < sets.Length; s++)
                        {
                            if (sets[s].Contains(prefab))
                                return true;
                        }
                    }
                }
            }
            finally
            {
                chunks.Dispose();
            }
            return false;
        }

        // ─────────────────────────────────────────────────────────────────────────────────
        // Prefab resolution — house pattern (fast PrefabID path + name-scan fallback,
        // tolerate missing: the lodge and surfaces are NOT YET IMPORTED; the system logs
        // its inertness once and sleeps until they exist).
        // ─────────────────────────────────────────────────────────────────────────────────
        private void ResolvePrefabs()
        {
            if (m_PrefabsResolved)
                return;
            m_PrefabsResolved = true;

            ResolveSet(LodgePrefabNames, nameof(BuildingPrefab), m_LodgePrefabs, "lodge");
            ResolveSet(GroomedSurfaceNames, "SurfacePrefab", m_GroomedPrefabs, "groomed surface");
            ResolveSet(SnowmakingSurfaceNames, "SurfacePrefab", m_SnowmakingPrefabs, "snowmaking surface");
            ResolveSet(NightSurfaceNames, "SurfacePrefab", m_NightPrefabs, "night surface");

            if (m_LodgePrefabs.Count == 0)
                Mod.Log.Info("Attractiveness: lodge prefab not present — system inert until " +
                             $"'{string.Join("', '", LodgePrefabNames)}' is imported (expected pre-import)");
        }

        private void ResolveSet(string[] names, string typeName, HashSet<Entity> target, string kind)
        {
            foreach (string name in names)
            {
                Entity prefabEntity = Entity.Null;
                if (m_PrefabSystem.TryGetPrefab(new PrefabID(typeName, name), out PrefabBase prefabBase))
                    m_PrefabSystem.TryGetEntity(prefabBase, out prefabEntity);

                if (prefabEntity == Entity.Null)
                {
                    var candidates = m_PrefabScanQuery.ToEntityArray(Allocator.Temp);
                    try
                    {
                        for (int i = 0; i < candidates.Length; i++)
                        {
                            if (m_PrefabSystem.GetPrefabName(candidates[i]) == name)
                            {
                                prefabEntity = candidates[i];
                                break;
                            }
                        }
                    }
                    finally
                    {
                        candidates.Dispose();
                    }
                }

                if (prefabEntity == Entity.Null)
                {
                    Mod.Log.Info($"Attractiveness: {kind} prefab '{name}' not found (tolerated)");
                    continue;
                }

                target.Add(prefabEntity);
                Mod.Log.Info($"Attractiveness: resolved {kind} prefab '{name}'");
            }
        }
    }
}
