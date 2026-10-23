// Unlocks the game's own area-props machinery (snow guns / fencing per polygon vertex) for our run surfaces: the vanilla spawner only
// fires for areas with a runtime Game.Objects.SubObject buffer, which LotPrefab adds and SurfacePrefab does not, so this system adds
// that buffer + Updated to run areas whose prefab has sub-object entries; vanilla does the rest.
// Phase is load-bearing: UpdateBefore<SecondaryObjectSystem> in Modification4B (the Created/Updated tags are wiped at Cleanup each frame).
// Game mode only, never Editor (spawned props are real owned entities a map author must not bake into a map).
// Prop density: two Options sliders (RunPropDensityRules) rewrite prefab-side probabilities at runtime; re-stamp only after a settled
// slider change, never at a load (member order is not shown stable across sessions). House contract: ARMED line, one-shot "!!!" catch.
// Engine notes: EngineNotes/SkiRunPropSystem.md

using System;
using System.Collections.Generic;
using System.Text;
using Colossal.Serialization.Entities;
using Game;
using Game.Areas;
using Game.Common;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public sealed partial class SkiRunPropSystem : GameSystemBase
    {
        // Same tolerant name resolution as the sibling run-aware systems. Groomed is watched too: it has no AreaSubObjects today, and the
        // prefab-side gate below makes that a no-op until the day it does. SkiRunNaturalA is deliberately absent: it has no AreaSubObjects, so
        // no border props (marker poles would need an Editor import). SkiRunCatTrackA is watched like groomed (no AreaSubObjects in the probe
        // prefab, so a no-op; its future hydrants-only entry, RunPropDensityRules.PropClassAllowed, is then bridged without a code change).
        private static readonly string[] RunSurfaceNames =
            { SnowmakingRunName, "SkiRunGroomedA", NightRunName, "SkiRunCatTrackA" };

        // The two run prefabs whose border props the Options sliders thin (engine notes "Prop density").
        private const string SnowmakingRunName = "SkiRunSnowmakingA";
        private const string NightRunName = "SkiRunNightA";

        private PrefabSystem m_PrefabSystem;
        private EntityQuery m_UnbridgedAreaQuery;
        private EntityQuery m_PrefabScanQuery;

        private readonly HashSet<Entity> m_RunPrefabs = new HashSet<Entity>();
        private bool m_PrefabsResolved;            // session cache (PrefabSystem outlives loads)

        // Per-load state: reset and re-armed at OnGameLoadingComplete.
        private bool m_Armed;
        private bool m_Disabled;
        private bool m_ErrorLogged;
        private int m_BridgedCount;

        // --- Prop density ---------------------------------------------------------------------------
        /// <summary>One usable border entry of a run prefab: its index in the prefab's Game.Prefabs.SubObject buffer
        /// and the chance recorded before any write.</summary>
        private struct DensityEntry
        {
            public int Index;
            public int Base;
        }

        /// <summary>One member of a run prop group: base weight recorded before any write, census class, and its
        /// place in the two groups (NightOnly = in a night group and in no snowmaking group: a mast; InNight = a
        /// snowmaking member that is also a night member, i.e. shared).</summary>
        private struct DensityMember
        {
            public Entity Prefab;
            public string Name;
            public int Base;
            public bool NightOnly;
            public bool InNight;
            public byte Class;
        }

        // Session cache: set once, before any write (prefab entities and our writes outlive loads).
        private Entity m_SnowmakingPrefab;          // set by name in ResolveRunPrefabs
        private Entity m_NightPrefab;
        private bool m_DensityResolved;             // bases recorded (ResolveDensity completed once)
        private bool m_DensityWritten;              // prefab data may differ from the bases (write-back due at a non-Game load)
        private readonly List<DensityEntry> m_SnowEntries = new List<DensityEntry>();
        private readonly List<DensityEntry> m_NightEntries = new List<DensityEntry>();
        private readonly List<DensityMember> m_NightMembers = new List<DensityMember>();   // group order (the write order)
        private readonly List<DensityMember> m_SnowMembers = new List<DensityMember>();
        private readonly List<int> m_MastOrder = new List<int>();   // night-only members: base weight descending, ties by ordinal name
        private readonly Dictionary<Entity, byte> m_ClassOf = new Dictionary<Entity, byte>();
        private int m_BaseShared;                   // NM: base weight of the night group's shared members
        private int m_BaseMast;                     // M: base weight of its own members (masts)
        private int m_SnowBaseTotal;                // the snowmaking group's base weight (ARMED)
        private int m_SharedCount;                  // night members that are shared (the "shared ... weights 0" clause)

        // Per load.
        private EntityQuery m_PropRunQuery;
        private bool m_DensityLive;                 // a Game load with something recorded
        private bool m_DensityForce;                // the load apply is still due
        private bool m_DensityDisabled;             // tripped by the density catch; re-armed per load
        private bool m_DensityErrorLogged;          // one density "!!!" per load
        private bool m_CensusDue;                   // the frame after a re-stamp with K > 0
        private int m_SeenGun = -1, m_SeenMast = -1;
        private int m_AppliedGun = -1, m_AppliedMast = -1;
        private int m_Stable;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();

            // Areas that could still need the bridge. The marker of a fully bridged area is PseudoRandomSeed (added last), not the SubObject
            // buffer, so runs bridged by an earlier buffer-only version are picked up again and completed. Once done they leave the query, so
            // steady state is empty and the early-out is near-free. Only LotPrefab adds PseudoRandomSeed (Game.Prefabs/LotPrefab.cs:37), so
            // Game.Areas.Surface (SurfacePrefab.cs:21-23, as m_PropRunQuery below) keeps map tiles, districts and vanilla surfaces out: only a
            // run of ours that is not yet fully bridged remains.
            m_UnbridgedAreaQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Area>(),
                    ComponentType.ReadOnly<Game.Areas.Surface>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                    ComponentType.ReadOnly<Node>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<PseudoRandomSeed>(),
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Temp>(),
                },
            });
            m_PrefabScanQuery = GetEntityQuery(ComponentType.ReadOnly<PrefabData>());

            // Our fully bridged run surfaces (the re-stamp and the census). Game.Areas.Surface (the SurfacePrefab archetype, SurfacePrefab.cs:21)
            // keeps building lots out: lots get SubObject and PseudoRandomSeed from their class archetype; vanilla building surfaces have no
            // runtime SubObject buffer. The per-entity prefab filter (StampRuns, LogCensus) keeps only our two density prefabs.
            m_PropRunQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Area>(),
                    ComponentType.ReadOnly<Game.Areas.Surface>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                    ComponentType.ReadOnly<Node>(),
                    ComponentType.ReadOnly<Game.Objects.SubObject>(),
                    ComponentType.ReadOnly<PseudoRandomSeed>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Temp>(),
                },
            });

            Mod.Log.Info($"{nameof(SkiRunPropSystem)} created");
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);

            m_Armed = false;
            m_Disabled = false;
            m_ErrorLogged = false;
            m_BridgedCount = 0;

            // HARD GATE: Game only — spawned props must never be baked into a map. (Prop density: at a
            // non-Game load LoadDensity only writes the base values back after a write, hygiene.)
            if (mode != GameMode.Game)
            {
                LoadDensity(mode);
                return;
            }

            try
            {
                ResolveRunPrefabs();
                if (m_RunPrefabs.Count == 0)
                {
                    Mod.Log.Info("Props: no run surface prefab present — vertex-prop bridge " +
                                 "inert until one is imported (expected pre-import)");
                }
                else
                {
                    m_Armed = true;
                    Mod.Log.Info($"Props: ARMED — bridging the runtime SubObject buffer onto run " +
                                 $"surfaces whose prefab declares AreaSubObjects ({m_RunPrefabs.Count} " +
                                 "prefab(s) watched); the VANILLA SecondaryObjectSystem then places " +
                                 "at most one border prop per drawn polygon vertex (Options prop density, " +
                                 "look only), self-healing on edits");
                }
            }
            catch (Exception e)
            {
                m_Disabled = true;
                if (!m_ErrorLogged)
                {
                    m_ErrorLogged = true;
                    Mod.Log.Error($"!!! {nameof(SkiRunPropSystem)} EXCEPTION during load setup — " +
                                  "vertex-prop bridge disabled until the next game load. Please " +
                                  $"report this stack:\n{e}");
                }
            }

            // After the bridge's setup (ResolveRunPrefabs found the two run prefabs by name), in its own try/catch, so neither catch stops the
            // other.
            LoadDensity(mode);
        }

        protected override void OnUpdate()
        {
            // Density first, before the bridge's m_Disabled return: its own try/catch, and this frame's bridging and SecondaryObjectSystem see
            // the values it writes.
            UpdateDensity();

            try
            {
                if (m_Disabled || !m_Armed || !ResortPresence.Present || m_UnbridgedAreaQuery.IsEmptyIgnoreFilter)
                    return;

                // Collect first, mutate after — structural changes invalidate chunks.
                NativeList<Entity> toBridge = default;
                var chunks = m_UnbridgedAreaQuery.ToArchetypeChunkArray(Allocator.Temp);
                try
                {
                    var entityHandle = GetEntityTypeHandle();
                    var prefabRefHandle = GetComponentTypeHandle<PrefabRef>(isReadOnly: true);
                    for (int c = 0; c < chunks.Length; c++)
                    {
                        var entities = chunks[c].GetNativeArray(entityHandle);
                        var prefabRefs = chunks[c].GetNativeArray(ref prefabRefHandle);
                        for (int i = 0; i < prefabRefs.Length; i++)
                        {
                            Entity prefab = prefabRefs[i].m_Prefab;
                            if (!m_RunPrefabs.Contains(prefab))
                                continue;
                            // Prefab-side gate: only bridge when there is actually
                            // something to spawn (AreaSubObjects filled the prefab's
                            // sub-object buffer at prefab initialization).
                            if (!EntityManager.HasBuffer<Game.Prefabs.SubObject>(prefab) ||
                                EntityManager.GetBuffer<Game.Prefabs.SubObject>(prefab, isReadOnly: true).Length == 0)
                                continue;
                            if (!toBridge.IsCreated)
                                toBridge = new NativeList<Entity>(8, Allocator.Temp);
                            toBridge.Add(entities[i]);
                        }
                    }
                }
                finally
                {
                    chunks.Dispose();
                }

                if (!toBridge.IsCreated)
                    return;

                for (int i = 0; i < toBridge.Length; i++)
                {
                    Entity area = toBridge[i];
                    bool addedBuffer = false;
                    if (!EntityManager.HasBuffer<Game.Objects.SubObject>(area))
                    {
                        EntityManager.AddBuffer<Game.Objects.SubObject>(area);
                        addedBuffer = true;
                    }

                    // The variety seed. LotPrefab's archetype hard-adds PseudoRandomSeed; SurfacePrefab's does not, so SecondaryObjectSystem falls back
                    // to a fresh per-run seed (SecondaryObjectSystem.cs:1120 picks the PseudoRandomSeed branch only when the component exists): every
                    // geometry edit would reshuffle every prop on the run. We supply a seed hashed from the area's first node position: deterministic,
                    // unique per run, stable across loads and across edits elsewhere on the polygon, and it serializes with the save like any vanilla
                    // seed.
                    ushort seed = 1;
                    var nodes = EntityManager.GetBuffer<Node>(area, isReadOnly: true);
                    if (nodes.Length > 0)
                        seed = HashSeed(nodes[0].m_Position);
                    EntityManager.AddComponentData(area, new PseudoRandomSeed(seed));

                    // Seed the weather struct at zero in the same pass. The seed stays for the run's own props: the prop shader's snow lookup walks a
                    // prop's Owner chain until it finds Game.Objects.Surface (Game.Rendering/BatchDataSystem.cs:795-817, identical in 1.6.2), and a border
                    // prop's Owner is this run; without the seed, a freshly bridged run's props would read no surface state (the walk stops at the
                    // un-owned run) for up to 256 frames until SkiRunSurfaceSystem's first write. Runs carry no Owner (the run -> sign link is
                    // SkiRunBinding). That system sets both fields every pass, so a zero seed is never fought over.
                    EntityManager.AddComponent<Game.Objects.Surface>(area);

                    // Pre-existing areas (older saves) carry no Updated this frame —
                    // add it so SecondaryObjectSystem processes them once, right after
                    // us in this same Modification4B pass. Freshly drawn areas already
                    // have it; AddComponent on an existing component is a no-op.
                    EntityManager.AddComponent<Updated>(area);
                    m_BridgedCount++;
                    Mod.Log.Info($"Props: bridged run area {area.Index}:{area.Version} " +
                                 $"(#{m_BridgedCount} this load) — " +
                                 (addedBuffer ? "runtime SubObject buffer added, " : "buffer already present, ") +
                                 $"variety seed {seed} set, Updated ensured; vanilla border " +
                                 "placement takes it from here");
                }
                toBridge.Dispose();
            }
            catch (Exception e)
            {
                m_Disabled = true;
                if (!m_ErrorLogged)
                {
                    m_ErrorLogged = true;
                    Mod.Log.Error($"!!! {nameof(SkiRunPropSystem)} EXCEPTION — vertex-prop " +
                                  "bridge disabled until the next game load. Please report " +
                                  $"this stack:\n{e}");
                }
            }
        }

        /// <summary>Deterministic ushort seed from a world position, quantized to 0.1 m so
        /// float jitter cannot change it between loads. Integer mix, no state.</summary>
        private static ushort HashSeed(float3 position)
        {
            unchecked
            {
                uint h = 2166136261u;
                h = (h ^ (uint)(int)math.round(position.x * 10f)) * 16777619u;
                h = (h ^ (uint)(int)math.round(position.y * 10f)) * 16777619u;
                h = (h ^ (uint)(int)math.round(position.z * 10f)) * 16777619u;
                h ^= h >> 15;
                // 0 is a valid seed value but GetRandom clamps it away; keep 1..65535
                // so the logged number always matches what the game actually uses.
                return (ushort)math.max(1u, h & 0xFFFFu);
            }
        }

        // House pattern: fast PrefabID path + name-scan fallback, tolerate missing.
        private void ResolveRunPrefabs()
        {
            if (m_PrefabsResolved)
                return;
            m_PrefabsResolved = true;

            foreach (string name in RunSurfaceNames)
            {
                Entity prefabEntity = Entity.Null;
                if (m_PrefabSystem.TryGetPrefab(new PrefabID("SurfacePrefab", name),
                        out PrefabBase prefabBase))
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
                    Mod.Log.Info($"Props: run surface prefab '{name}' not found (tolerated)");
                    continue;
                }
                m_RunPrefabs.Add(prefabEntity);
                // The two prefabs the sliders act on, by name.
                if (name == SnowmakingRunName)
                    m_SnowmakingPrefab = prefabEntity;
                else if (name == NightRunName)
                    m_NightPrefab = prefabEntity;
                Mod.Log.Info($"Props: resolved run surface prefab '{name}'");
            }
        }


        /// <summary>The Options sliders, clamped (null settings → the default 100 %).</summary>
        private static int ReadGunPct() =>
            RunPropDensityRules.ClampPct(Mod.Settings?.SnowGunDensityPct ?? RunPropDensityRules.PctDefault);

        private static int ReadMastPct() =>
            RunPropDensityRules.ClampPct(Mod.Settings?.FloodlightDensityPct ?? RunPropDensityRules.PctDefault);

        private bool DensityRecorded => m_SnowEntries.Count > 0 || m_NightEntries.Count > 0;

        /// <summary>Per-load density setup (own try/catch). Non-Game load: write the bases back if a write happened.
        /// Game load: record the bases once per session, then the density ARMED line or the inert variant.</summary>
        private void LoadDensity(GameMode mode)
        {
            m_DensityLive = false;
            m_DensityForce = true;
            m_DensityDisabled = false;
            m_DensityErrorLogged = false;
            m_CensusDue = false;
            m_SeenGun = m_SeenMast = -1;
            m_AppliedGun = m_AppliedMast = -1;
            m_Stable = 0;

            try
            {
                if (mode != GameMode.Game)
                {
                    if (m_DensityWritten)
                    {
                        WriteBaseValues();
                        Mod.Log.Info($"Props: density written back to the prefab values ({mode}: no run props " +
                                     "outside a game; hygiene only)");
                    }
                    return;
                }

                ResolveDensity();
                int g = ReadGunPct();
                int m = ReadMastPct();
                if (!DensityRecorded)
                {
                    string reason = m_SnowmakingPrefab == Entity.Null && m_NightPrefab == Entity.Null
                        ? $"the run prefabs {SnowmakingRunName} and {NightRunName} were not found"
                        : "no border entry with a per-corner group";
                    Mod.Log.Info($"Props: density inert: {reason} (Options snow guns and hydrants {g} %, " +
                                 $"floodlight masts {m} %: no effect this load)");
                    return;
                }

                m_DensityLive = true;
                Mod.Log.Info($"Props: density ARMED — Options snow guns and hydrants {g} %, floodlight masts {m} % " +
                             "(look only: snow and upkeep follow the run type); base chance snowmaking " +
                             $"{BaseChances(m_SnowEntries)}, night {BaseChances(m_NightEntries)}; night group " +
                             $"{m_NightMembers.Count} member(s), weight {m_BaseShared} shared + {m_BaseMast} masts " +
                             $"({MastList(-1, -1)}); snowmaking group {m_SnowMembers.Count} member(s), weight " +
                             $"{m_SnowBaseTotal}");
            }
            catch (Exception e)
            {
                DensityFailed(e);
            }
        }

        /// <summary>Every frame of a live Game load (own try/catch): the census when due, then the settle rule; an
        /// apply writes the prefab values and, after a settled slider change (not at the load), re-stamps the runs.</summary>
        private void UpdateDensity()
        {
            if (!m_DensityLive || m_DensityDisabled)
                return;
            try
            {
                if (m_CensusDue)
                {
                    m_CensusDue = false;
                    LogCensus();
                }

                int g = ReadGunPct();
                int m = ReadMastPct();
                bool changed = g != m_SeenGun || m != m_SeenMast;
                m_SeenGun = g;
                m_SeenMast = m;
                m_Stable = RunPropDensityRules.NextStable(m_Stable, changed);
                if (!RunPropDensityRules.ApplyDue(g, m, m_AppliedGun, m_AppliedMast, m_Stable, m_DensityForce))
                    return;

                bool atLoad = m_DensityForce;
                m_DensityForce = false;
                WriteDensity(g, m, out int prefabs, out int changedValues);
                m_AppliedGun = g;
                m_AppliedMast = m;
                // never at a load (saved runs keep their props until edited); after a settled change, every run.
                int stamped = atLoad ? 0 : StampRuns();
                m_CensusDue = stamped > 0;

                string sharedZero = g == 0 && m_SharedCount > 0 ? "; shared gun, hydrant and empty weights 0" : "";
                string tail = atLoad
                    ? "runs not re-stamped (load: saved runs keep their props until edited)"
                    : $"{stamped} run(s) re-stamped (setting change)";
                Mod.Log.Info($"Props: density snow guns {g} %, floodlights {m} % applied to {prefabs} prefab(s), " +
                             $"{changedValues} value(s) changed (chance snowmaking {Chances(m_SnowEntries, g, m, false)}, " +
                             $"night {Chances(m_NightEntries, g, m, true)}; mast weights {MastList(g, m)}{sharedZero}); {tail}");
            }
            catch (Exception e)
            {
                DensityFailed(e);
            }
        }

        /// <summary>The density catch: one "!!!" per load, density off until the next game load; the bridge keeps
        /// working (its own flags are untouched).</summary>
        private void DensityFailed(Exception e)
        {
            m_DensityDisabled = true;
            m_CensusDue = false;
            if (m_DensityErrorLogged)
                return;
            m_DensityErrorLogged = true;
            Mod.Log.Error($"!!! {nameof(SkiRunPropSystem)} density EXCEPTION — prop density off until the next game " +
                          "load (runs keep the props they have; the vertex-prop bridge keeps working). Please report " +
                          $"this stack:\n{e}");
        }

        /// <summary>Once per session, at a Game load, before any write: record the usable border entries of the two
        /// run prefabs and their groups' members with base weights (engine notes PROP DENSITY). Not a write. Marked done
        /// only at the end, so a throw part-way is retried at the next load (no write happens before it completes).</summary>
        private void ResolveDensity()
        {
            if (m_DensityResolved)
                return;

            m_SnowEntries.Clear();
            m_NightEntries.Clear();
            m_NightMembers.Clear();
            m_SnowMembers.Clear();
            m_MastOrder.Clear();
            m_ClassOf.Clear();
            m_BaseShared = m_BaseMast = m_SnowBaseTotal = m_SharedCount = 0;

            var snowGroups = new List<Entity>();
            var nightGroups = new List<Entity>();
            RecordEntries(m_SnowmakingPrefab, SnowmakingRunName, m_SnowEntries, snowGroups);
            RecordEntries(m_NightPrefab, NightRunName, m_NightEntries, nightGroups);

            // Members: PlaceholderObjectElement.m_Object, filled at prefab init in prefab-init order
            // (ObjectInitializeSystem.cs:900-905, 1.6.2 :910-915); weights SpawnableObjectData.m_Probability (:913, 1.6.2
            // :923). Every member joins through its own SpawnableObject there, so it has the component the pick reads for
            // each member (SecondaryObjectSystem.cs:3241); one without it is skipped here, never written.
            var snowList = CollectMembers(snowGroups);
            var nightList = CollectMembers(nightGroups);
            var snowSet = new HashSet<Entity>(snowList);
            var nightSet = new HashSet<Entity>(nightList);
            int nightOnlyCount = 0;

            for (int i = 0; i < nightList.Count; i++)
            {
                Entity member = nightList[i];
                bool nightOnly = !snowSet.Contains(member);
                string name = m_PrefabSystem.GetPrefabName(member);
                int baseWeight = EntityManager.GetComponentData<SpawnableObjectData>(member).m_Probability;
                byte cls = RunPropDensityRules.ClassOf(name, nightOnly);
                m_NightMembers.Add(new DensityMember
                {
                    Prefab = member, Name = name, Base = baseWeight, NightOnly = nightOnly, InNight = true, Class = cls,
                });
                m_ClassOf[member] = cls;
                if (nightOnly)
                {
                    m_BaseMast += baseWeight;
                    nightOnlyCount++;
                    m_MastOrder.Add(m_NightMembers.Count - 1);
                }
                else
                {
                    m_BaseShared += baseWeight;
                    m_SharedCount++;
                }
            }

            for (int i = 0; i < snowList.Count; i++)
            {
                Entity member = snowList[i];
                string name = m_PrefabSystem.GetPrefabName(member);
                int baseWeight = EntityManager.GetComponentData<SpawnableObjectData>(member).m_Probability;
                byte cls = RunPropDensityRules.ClassOf(name, false);
                m_SnowMembers.Add(new DensityMember
                {
                    Prefab = member, Name = name, Base = baseWeight, NightOnly = false, InNight = nightSet.Contains(member),
                    Class = cls,
                });
                if (!m_ClassOf.ContainsKey(member))
                    m_ClassOf[member] = cls;
                m_SnowBaseTotal += baseWeight;
            }

            // Mast lists print by name, base weight descending, ties by ordinal name, any count.
            m_MastOrder.Sort((a, b) =>
            {
                int byWeight = m_NightMembers[b].Base.CompareTo(m_NightMembers[a].Base);
                return byWeight != 0 ? byWeight : string.CompareOrdinal(m_NightMembers[a].Name, m_NightMembers[b].Name);
            });

            if (m_NightEntries.Count > 0 && (nightOnlyCount == 0 || m_BaseMast <= 0))
                Mod.Log.Warn("Props: density WARN — the night group has no members of its own (masts); the floodlight " +
                             "slider does nothing and night runs follow the snow gun slider");

            m_DensityResolved = true;
        }

        /// <summary>The usable border entries of one run prefab: EdgePlacement entries (the ones
        /// CreateSecondaryAreaObjects places per corner, SecondaryObjectSystem.cs:2983) whose prefab is a placeholder
        /// group with a per-corner group index (m_RandomizeGroupIndex, :3075-3077). A group without it, or a direct
        /// object, caches one stream per run and replays it at every corner (:3047-3068), so a chance there acts on the
        /// whole run: skipped, with the once-per-session WARN when the prefab keeps no entry.</summary>
        private void RecordEntries(Entity prefab, string prefabName, List<DensityEntry> entries, List<Entity> groups)
        {
            if (prefab == Entity.Null)
                return;   // "Props: run surface prefab '…' not found (tolerated)" was logged by ResolveRunPrefabs

            var candidates = new List<Game.Prefabs.SubObject>();
            var indices = new List<int>();
            if (EntityManager.HasBuffer<Game.Prefabs.SubObject>(prefab))
            {
                var buffer = EntityManager.GetBuffer<Game.Prefabs.SubObject>(prefab, isReadOnly: true);
                for (int i = 0; i < buffer.Length; i++)
                {
                    if ((buffer[i].m_Flags & SubObjectFlags.EdgePlacement) == 0)
                        continue;
                    candidates.Add(buffer[i]);
                    indices.Add(i);
                }
            }

            int before = entries.Count;
            for (int i = 0; i < candidates.Count; i++)
            {
                Entity group = candidates[i].m_Prefab;
                if (group == Entity.Null ||
                    !EntityManager.HasComponent<PlaceholderObjectData>(group) ||
                    !EntityManager.HasBuffer<PlaceholderObjectElement>(group) ||
                    !EntityManager.GetComponentData<PlaceholderObjectData>(group).m_RandomizeGroupIndex)
                    continue;
                entries.Add(new DensityEntry { Index = indices[i], Base = candidates[i].m_Probability });
                if (!groups.Contains(group))
                    groups.Add(group);
            }

            if (entries.Count == before)
                Mod.Log.Warn($"Props: density WARN — {prefabName} has no border entry with a per-corner group; its " +
                             "slider does nothing");
        }

        /// <summary>The members of the given groups in group order, each once, only those with SpawnableObjectData.</summary>
        private List<Entity> CollectMembers(List<Entity> groups)
        {
            var result = new List<Entity>();
            var seen = new HashSet<Entity>();
            for (int g = 0; g < groups.Count; g++)
            {
                var elements = EntityManager.GetBuffer<PlaceholderObjectElement>(groups[g], isReadOnly: true);
                var objects = new List<Entity>(elements.Length);
                for (int i = 0; i < elements.Length; i++)
                    objects.Add(elements[i].m_Object);
                for (int i = 0; i < objects.Count; i++)
                {
                    Entity member = objects[i];
                    if (member == Entity.Null || !EntityManager.HasComponent<SpawnableObjectData>(member))
                        continue;
                    if (seen.Add(member))
                        result.Add(member);
                }
            }
            return result;
        }

        /// <summary>Writes the chances and the night members' weights for (g, m) through EntityManager (main thread;
        /// completes the jobs reading them). N = prefab entities written, C = values that changed.</summary>
        private void WriteDensity(int g, int m, out int prefabs, out int changedValues)
        {
            m_DensityWritten = true;   // before the first write: a throw part-way is still written back later
            prefabs = 0;
            changedValues = 0;

            if (m_SnowEntries.Count > 0)
            {
                prefabs++;
                changedValues += WriteChances(m_SnowmakingPrefab, m_SnowEntries, g, m, false, false);
            }
            if (m_NightEntries.Count > 0)
            {
                prefabs++;
                changedValues += WriteChances(m_NightPrefab, m_NightEntries, g, m, true, false);
            }
            for (int i = 0; i < m_NightMembers.Count; i++)
            {
                DensityMember member = m_NightMembers[i];
                prefabs++;
                if (WriteWeight(member.Prefab, CurrentNightWeight(member, g, m)))
                    changedValues++;
            }
        }

        /// <summary>Writes every recorded base value back (the non-Game load hygiene).</summary>
        private void WriteBaseValues()
        {
            if (m_SnowEntries.Count > 0)
                WriteChances(m_SnowmakingPrefab, m_SnowEntries, 0, 0, false, true);
            if (m_NightEntries.Count > 0)
                WriteChances(m_NightPrefab, m_NightEntries, 0, 0, true, true);
            for (int i = 0; i < m_NightMembers.Count; i++)
                WriteWeight(m_NightMembers[i].Prefab, m_NightMembers[i].Base);
            m_DensityWritten = false;
        }

        /// <summary>One run prefab's border entries: the density chance, or the base when restoring. Returns the count
        /// of values that changed.</summary>
        private int WriteChances(Entity prefab, List<DensityEntry> entries, int g, int m, bool night, bool restore)
        {
            int changed = 0;
            var buffer = EntityManager.GetBuffer<Game.Prefabs.SubObject>(prefab);
            for (int i = 0; i < entries.Count; i++)
            {
                DensityEntry entry = entries[i];
                if (entry.Index >= buffer.Length)
                    continue;   // the buffer is filled once per session (AreaInitializeSystem.cs:235-247); never seen shorter
                int value = restore ? entry.Base : ChanceOf(entry, g, m, night);
                Game.Prefabs.SubObject element = buffer[entry.Index];
                if (element.m_Probability == value)
                    continue;
                element.m_Probability = value;
                buffer[entry.Index] = element;
                changed++;
            }
            return changed;
        }

        /// <summary>One member's SpawnableObjectData.m_Probability; only that field changes. True when it changed.</summary>
        private bool WriteWeight(Entity member, int weight)
        {
            var data = EntityManager.GetComponentData<SpawnableObjectData>(member);
            if (data.m_Probability == weight)
                return false;
            data.m_Probability = weight;
            EntityManager.SetComponentData(member, data);
            return true;
        }

        private int ChanceOf(DensityEntry entry, int g, int m, bool night) => night
            ? RunPropDensityRules.NightChance(entry.Base, g, m, m_BaseShared, m_BaseMast)
            : RunPropDensityRules.SnowmakingChance(entry.Base, g);

        private static int CurrentNightWeight(DensityMember member, int g, int m) => member.NightOnly
            ? RunPropDensityRules.MastWeight(member.Base, g, m)
            : RunPropDensityRules.NonMastWeight(member.Base, g);

        /// <summary>A snowmaking member's weight under (g, m): shared members follow the night write, the others keep
        /// their base (nothing writes them).</summary>
        private static int CurrentSnowWeight(DensityMember member, int g) => member.InNight
            ? RunPropDensityRules.NonMastWeight(member.Base, g)
            : member.Base;

        private bool IsDensityRun(Entity prefab) =>
            prefab != Entity.Null &&
            ((prefab == m_SnowmakingPrefab && m_SnowEntries.Count > 0) || (prefab == m_NightPrefab && m_NightEntries.Count > 0));

        /// <summary>Tags every fully bridged run of our two density prefabs with Game.Areas.ChangedDistrict so the
        /// VANILLA SecondaryObjectSystem, right after us this frame, re-runs its props with the new values and removes
        /// the tag (engine notes PROP DENSITY). Returns K.</summary>
        private int StampRuns()
        {
            var runs = m_PropRunQuery.ToEntityArray(Allocator.Temp);
            var stamp = new List<Entity>(runs.Length);
            try
            {
                for (int i = 0; i < runs.Length; i++)
                {
                    if (IsDensityRun(EntityManager.GetComponentData<PrefabRef>(runs[i]).m_Prefab))
                        stamp.Add(runs[i]);
                }
            }
            finally
            {
                runs.Dispose();
            }
            // Mutate after collecting: structural changes invalidate the query's chunks.
            for (int i = 0; i < stamp.Count; i++)
                EntityManager.AddComponent<ChangedDistrict>(stamp[i]);
            return stamp.Count;
        }

        /// <summary>The census, the frame after a re-stamp: per run type, corners (Node buffer length), the run's own
        /// secondary props by class (Secondary + Owner == the run, class by prefab), "nothing" = corners without one,
        /// and the expected count from the current chance and weights (RunPropDensityRules.ExpectedTenths).</summary>
        private void LogCensus()
        {
            int sRuns = 0, sCorners = 0, sGun = 0, sHydrant = 0, sEmpty = 0, sNothing = 0;
            int nRuns = 0, nCorners = 0, nGun = 0, nHydrant = 0, nMast = 0, nEmpty = 0, nNothing = 0;
            int other = 0;
            var subs = new List<Entity>();

            var runs = m_PropRunQuery.ToEntityArray(Allocator.Temp);
            try
            {
                for (int r = 0; r < runs.Length; r++)
                {
                    Entity run = runs[r];
                    Entity prefab = EntityManager.GetComponentData<PrefabRef>(run).m_Prefab;
                    bool snow = prefab != Entity.Null && prefab == m_SnowmakingPrefab && m_SnowEntries.Count > 0;
                    bool night = !snow && prefab != Entity.Null && prefab == m_NightPrefab && m_NightEntries.Count > 0;
                    if (!snow && !night)
                        continue;

                    int corners = EntityManager.GetBuffer<Node>(run, isReadOnly: true).Length;
                    subs.Clear();
                    var buffer = EntityManager.GetBuffer<Game.Objects.SubObject>(run, isReadOnly: true);
                    for (int i = 0; i < buffer.Length; i++)
                        subs.Add(buffer[i].m_SubObject);

                    int objects = 0;
                    for (int i = 0; i < subs.Count; i++)
                    {
                        Entity e = subs[i];
                        if (!EntityManager.Exists(e) || EntityManager.HasComponent<Deleted>(e) ||
                            !EntityManager.HasComponent<Game.Objects.Secondary>(e) ||
                            !EntityManager.HasComponent<Owner>(e) ||
                            EntityManager.GetComponentData<Owner>(e).m_Owner != run)
                            continue;
                        byte cls = RunPropDensityRules.ClassOther;
                        if (EntityManager.HasComponent<PrefabRef>(e) &&
                            m_ClassOf.TryGetValue(EntityManager.GetComponentData<PrefabRef>(e).m_Prefab, out byte known))
                            cls = known;
                        objects++;
                        if (cls == RunPropDensityRules.ClassGun) { if (snow) sGun++; else nGun++; }
                        else if (cls == RunPropDensityRules.ClassHydrant) { if (snow) sHydrant++; else nHydrant++; }
                        else if (cls == RunPropDensityRules.ClassEmpty) { if (snow) sEmpty++; else nEmpty++; }
                        else if (cls == RunPropDensityRules.ClassMast && night) nMast++;
                        else other++;   // a mast on a snowmaking run, or a prop of no group of ours
                    }

                    int nothing = math.max(0, corners - objects);
                    if (snow)
                    {
                        sRuns++;
                        sCorners += corners;
                        sNothing += nothing;
                    }
                    else
                    {
                        nRuns++;
                        nCorners += corners;
                        nNothing += nothing;
                    }
                }
            }
            finally
            {
                runs.Dispose();
            }

            int g = m_AppliedGun, m = m_AppliedMast;
            int sTotal = 0, sGunHydrant = 0;
            for (int i = 0; i < m_SnowMembers.Count; i++)
            {
                int w = CurrentSnowWeight(m_SnowMembers[i], g);
                sTotal += w;
                if (m_SnowMembers[i].Class == RunPropDensityRules.ClassGun || m_SnowMembers[i].Class == RunPropDensityRules.ClassHydrant)
                    sGunHydrant += w;
            }
            int nTotal = 0, nGunHydrant = 0, nMasts = 0;
            for (int i = 0; i < m_NightMembers.Count; i++)
            {
                int w = CurrentNightWeight(m_NightMembers[i], g, m);
                nTotal += w;
                if (m_NightMembers[i].NightOnly)
                    nMasts += w;
                else if (m_NightMembers[i].Class == RunPropDensityRules.ClassGun || m_NightMembers[i].Class == RunPropDensityRules.ClassHydrant)
                    nGunHydrant += w;
            }
            int sChance = m_SnowEntries.Count > 0 ? ChanceOf(m_SnowEntries[0], g, m, false) : 0;
            int nChance = m_NightEntries.Count > 0 ? ChanceOf(m_NightEntries[0], g, m, true) : 0;
            int xs = RunPropDensityRules.ExpectedTenths(sCorners, sChance, sGunHydrant, sTotal);
            int xn = RunPropDensityRules.ExpectedTenths(nCorners, nChance, nGunHydrant, nTotal);
            int xm = RunPropDensityRules.ExpectedTenths(nCorners, nChance, nMasts, nTotal);

            Mod.Log.Info($"Props: census — snowmaking {sRuns} run(s), {sCorners} corner(s): guns {sGun}, hydrants {sHydrant}, " +
                         $"empty markers {sEmpty}, nothing {sNothing} (expected guns+hydrants {Tenths(xs)}); night {nRuns} " +
                         $"run(s), {nCorners} corner(s): guns {nGun}, hydrants {nHydrant}, masts {nMast}, empty markers " +
                         $"{nEmpty}, nothing {nNothing} (expected guns+hydrants {Tenths(xn)}, masts {Tenths(xm)})" +
                         (other > 0 ? $", other {other}" : "") +
                         // SkiPropNone is mesh-less; if the game gives it no valid object archetype (SecondaryObjectSystem.cs:3552-3556 returns silently), its
                         // corners read as nothing.
                         (sNothing > 0 ? " (empty markers not instantiated read as nothing)" : ""));
        }

        /// <summary>The recorded base chances of one run prefab's entries ("100"; "a/b" for several; "none").</summary>
        private static string BaseChances(List<DensityEntry> entries)
        {
            if (entries.Count == 0)
                return "none";
            var sb = new StringBuilder();
            for (int i = 0; i < entries.Count; i++)
            {
                if (i > 0)
                    sb.Append('/');
                sb.Append(entries[i].Base);
            }
            return sb.ToString();
        }

        /// <summary>The chances written for (g, m), same format as BaseChances.</summary>
        private string Chances(List<DensityEntry> entries, int g, int m, bool night)
        {
            if (entries.Count == 0)
                return "none";
            var sb = new StringBuilder();
            for (int i = 0; i < entries.Count; i++)
            {
                if (i > 0)
                    sb.Append('/');
                sb.Append(ChanceOf(entries[i], g, m, night));
            }
            return sb.ToString();
        }

        /// <summary>"SkiPisteFloodlight 100, SkiPisteFloodlightLow 28, SkiPisteFloodlightTall 22": the masts by name in
        /// base-weight-descending order (ties by ordinal name), any count; base weights when g &lt; 0, else the weights
        /// written for (g, m); "none" without masts.</summary>
        private string MastList(int g, int m)
        {
            if (m_MastOrder.Count == 0)
                return "none";
            var sb = new StringBuilder();
            for (int i = 0; i < m_MastOrder.Count; i++)
            {
                DensityMember mast = m_NightMembers[m_MastOrder[i]];
                if (i > 0)
                    sb.Append(", ");
                sb.Append(mast.Name).Append(' ').Append(g < 0 ? mast.Base : CurrentNightWeight(mast, g, m));
            }
            return sb.ToString();
        }

        /// <summary>Tenths as "x.y" (non-negative).</summary>
        private static string Tenths(int tenths)
        {
            int t = tenths < 0 ? 0 : tenths;
            return (t / 10).ToString(System.Globalization.CultureInfo.InvariantCulture) + "." +
                   (t % 10).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
