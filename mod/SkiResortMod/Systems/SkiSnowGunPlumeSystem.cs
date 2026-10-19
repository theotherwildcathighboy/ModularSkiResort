// SkiSnowGunPlumeSystem — T46 (2026-10-17): snow guns show a vapour plume while their run's guns are running.
// Research: docs/research/snow-gun-plume-2026-10-17.md; build note docs/research/field/2026-10-15/build/T46-snow-gun-plume.md.
//
// THE MECHANISM (decompile-verified; citations 1.6.0f1; the Effects, BuildingLotRender, ValidationSystem, GenerateObjects and
// Events files are identical in 1.6.2f1; ValidationHelpers, SystemOrder and GameManager changed, the cited lines re-read there):
//   * The six gun object prefabs (SkiSnowGunTower / TowerLow / TowerTall / Fan / Lance / LanceTall) carry an EffectSource entry
//     for OUR EffectPrefab SkiSnowGunPlume (a VFX component on the vanilla WaterVaporFactorySmall graph, UnityObjectsMap
//     0f7db876…, its own m_MaxCount 256, m_RequiredFlags = Disaster 0x800). EffectSource adds EnabledEffect to the gun's
//     archetype (EffectSource.cs:56-59), so EffectControlSystem owns the plume (EffectControlSystem.cs:1087-1097).
//   * Disaster, for a gun (not a ServiceUpgrade, not an ExtractorFacility, so owner = top owner = the gun,
//     EffectControlData.cs:702-731), is `EarlyDisasterWarningDuration.m_EndFrame > simulation frame` on the gun
//     (EffectControlData.cs:419-424; the frame is SimulationSystem.frameIndex, EffectControlSystem.cs:1155). That component
//     has exactly one other user in Game.dll: Game.Events/InitializeSystem.cs:565, which writes it only to entities of
//     m_EDWSBuildingQuery = All<Game.Buildings.EarlyDisasterWarningSystem> (:280). On a gun it is inert: nothing else reads,
//     writes or removes it (grep of 1.6.0f1 and 1.6.2f1). No gun ever had it before T46, so a gun without it shows no plume.
//   * WHY NOT the vanilla WaterVaporFactorySmallVFX (Operational) + Game.Buildings.Extension (prop-effects.md §4.2): REJECTED.
//     BuildingLotRenderSystem's Burst job reads m_PrefabBuildingExtensionData[prefab] UNGUARDED for every chunk with Extension
//     and Temp / Error / Warning (BuildingLotRenderSystem.cs DrawObjects, the `flag2` branch); our gun prefabs have no
//     BuildingExtensionData, and ValidationSystem gives a PERMANENT colliding object Error (Game.Objects/ValidationHelpers.cs
//     CheckOverlap: m_PermanentEntity = the other object, severity Error by default; AddError in ValidationSystem) while the
//     player drags a road or a prop over a gun, and GenerateObjectsSystem copies Extension from an original to its Temp copy.
//     A missing-component lookup in a Burst job with safety checks off is undefined behaviour (a crash class). The research
//     doc §3 has the full chain.
//   * A write: set the gun's EarlyDisasterWarningDuration (added once, then SetComponentData) and, only when the visible
//     state flips, add Game.Common.EffectsUpdated: EffectControlSystem's updated query takes it (:1087-1097), its job queues
//     CheckEnabled for a near-camera owner (:224-231), and CleanUpSystem strips the tag at the end of the frame. Stamped here
//     in GameSimulation (LateUpdate), the tag lives into the NEXT frame's PreCulling, where EffectControlSystem runs
//     (SystemOrder.cs:636, 1.6.2 :637), and dies at that frame's Cleanup (GameManager.cs:2391 / :2399 / :2407: MainLoop,
//     Cleanup, LateUpdate; the
//     SkiNightLightSystem precedent, field-proven). An owner-culled plume of a gun off screen is disabled by the culling job
//     and re-checked when the gun comes near the camera (:298-353), so a change made while off screen shows on arrival.
//   * LEASE (SnowGunPlumeRules): a plume's end frame is 1.5 game hours ahead, renewed while it runs, cut to 0 when it stops.
//     Without this system (assets kept, code removed) every plume runs out by itself at the next re-check.
//
// THE RULE (one predicate with the injection; SnowGunPlumeRules header): a gun plumes when the Options toggle is on, its
// run's guns ran in the injection's last pass (SkiSnowInjectionSystem.TryGetRunGunsRunning: hysteresis state Running AND a
// cold texel; stopped at GunStopDepthM, natural snow that deep included; warm-spell protection never counts) and the gun's own
// elevation is cold (SnowLineSystem.GetLocalTemperature <= freezing + THE margin, the fold's per-texel test). FanIdle,
// hydrants and the folded lance carry no plume entry and are never touched.
//
// SCOPE: Game mode only; guns owned (Secondary + Owner) by OUR snowmaking / night run areas, through each run's runtime
// SubObject buffer (the buffer SkiRunPropSystem bridges; vanilla SecondaryObjectSystem fills it). Nothing else on the map is
// written. Cadence: every SnowGunPlumeRules.PlumeIntervalFrames (64) sim frames, anchored after the injection (Mod.cs; not
// load-bearing: we read the per-run flags it published, at most one window old). Cost: one pass over the guns of our runs
// (tens to a few hundred entities); writes only on a flip or a lease renewal.
//
// LOGS (silence must be impossible): one ARMED / inert line per load (prefabs found, plume entries found, the rule's numbers);
// per run, a line when its guns start or stop plumes (bounded by the hysteresis and the thermal line, a few per game day);
// a census line whenever the number of plumes changes, at most one per CensusWindowS of wall clock (a heartbeat when
// unchanged is not needed: the per-run lines carry the state); a WARN once per load when more guns want a plume than the
// effect's m_MaxCount (VFXSystem.cs:224 drops the rest silently, near-camera only); the house one-shot "!!!" catch that
// disables the system for the load. DUMP: the catalog's run rows carry the guns clause (SkiRunCatalogSystem.SnowClause).

using System;
using System.Collections.Generic;
using Colossal.Serialization.Entities;
using Game;
using Game.Areas;
using Game.Common;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;

namespace SkiResortMod.Systems
{
    public sealed partial class SkiSnowGunPlumeSystem : GameSystemBase
    {
        // The run surfaces whose border props are guns (SkiRunPropSystem's snowmaking and night groups).
        private static readonly string[] RunSurfaceNames = { "SkiRunSnowmakingA", "SkiRunNightA" };

        // The gun object prefabs that carry the plume entry (FanIdle, hydrants and SkiSnowGunLanceDown do not).
        private static readonly string[] GunNames =
        {
            "SkiSnowGunTower", "SkiSnowGunTowerLow", "SkiSnowGunTowerTall",
            "SkiSnowGunFan", "SkiSnowGunLance", "SkiSnowGunLanceTall",
        };

        // Our plume EffectPrefab (ImportedData/SkiSnowGunPlume).
        private const string PlumeEffectName = "SkiSnowGunPlume";

        private const float CensusWindowS = 10f;

        private SkiSnowInjectionSystem m_Injection;
        private SnowLineSystem m_SnowLine;
        private SimulationSystem m_Simulation;
        private PrefabSystem m_PrefabSystem;
        private EntityQuery m_RunQuery;
        private EntityQuery m_PrefabScanQuery;

        private readonly HashSet<Entity> m_RunPrefabs = new HashSet<Entity>();
        private readonly HashSet<Entity> m_GunPrefabs = new HashSet<Entity>();
        private Entity m_PlumeEffect;
        private bool m_Resolved;

        // Per load.
        private bool m_Armed;
        private bool m_Disabled;
        private bool m_ErrorLogged;
        private bool m_CapWarned;
        private readonly Dictionary<Entity, int> m_RunPlumes = new Dictionary<Entity, int>();   // last logged count per run
        private int m_LastTotal = -1;
        private int m_PendingTotal = -1;
        private float m_LastCensusAt = -1000f;

        // Scratch (main thread, reused).
        private readonly List<Entity> m_Write = new List<Entity>(64);
        private readonly List<uint> m_WriteEnd = new List<uint>(64);
        private readonly List<bool> m_WriteFlip = new List<bool>(64);
        private readonly List<Entity> m_Subs = new List<Entity>(64);

        protected override void OnCreate()
        {
            base.OnCreate();
            m_Injection = World.GetOrCreateSystemManaged<SkiSnowInjectionSystem>();
            m_SnowLine = World.GetOrCreateSystemManaged<SnowLineSystem>();
            m_Simulation = World.GetOrCreateSystemManaged<SimulationSystem>();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_RunQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Area>(),
                    // L2 (code review 2026-10-19): Lots also carry SubObject (LotPrefab.cs:36); Game.Areas.Surface
                    // (SurfacePrefab.cs:21-23) keeps them out of this per-pass ToEntityArray.
                    ComponentType.ReadOnly<Game.Areas.Surface>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                    ComponentType.ReadOnly<Game.Objects.SubObject>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Temp>(),
                },
            });
            m_PrefabScanQuery = GetEntityQuery(ComponentType.ReadOnly<PrefabData>());
            Mod.Log.Info($"{nameof(SkiSnowGunPlumeSystem)} created");
        }

        public override int GetUpdateInterval(SystemUpdatePhase phase)
        {
            return SnowGunPlumeRules.PlumeIntervalFrames;
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            m_Armed = false;
            m_Disabled = false;
            m_ErrorLogged = false;
            m_CapWarned = false;
            m_RunPlumes.Clear();
            m_LastTotal = -1;
            m_PendingTotal = -1;
            m_LastCensusAt = -1000f;
            if (mode != GameMode.Game)
                return;

            try
            {
                Resolve();
                int withEntry = 0;
                foreach (Entity gun in m_GunPrefabs)
                {
                    if (HasPlumeEntry(gun))
                        withEntry++;
                }
                if (m_PlumeEffect == Entity.Null || m_RunPrefabs.Count == 0 || withEntry == 0)
                {
                    Mod.Log.Info("Plume: inert — " +
                                 (m_PlumeEffect == Entity.Null ? $"effect prefab '{PlumeEffectName}' not found (prefab JSON not deployed?)"
                                  : m_RunPrefabs.Count == 0 ? "no snowmaking / night run prefab"
                                  : "no gun prefab carries the plume entry (prefab JSON not deployed?)") +
                                 $"; {m_GunPrefabs.Count} of {GunNames.Length} gun prefab(s) found");
                    return;
                }
                m_Armed = true;
                Mod.Log.Info($"Plume: ARMED — {withEntry} of {GunNames.Length} gun prefab(s) carry '{PlumeEffectName}' " +
                             $"({m_GunPrefabs.Count} found), {m_RunPrefabs.Count} run prefab(s) watched; a gun plumes while its " +
                             $"run's guns run (cold, pack below {RunSnowRules.GunStopDepthM:F2} m, restart below " +
                             $"{SnowGunPlumeRules.RestartBelowM:F2} m, natural snow counts) and the gun's own elevation is at or " +
                             $"below freezing + the snowmaking margin; checked every {SnowGunPlumeRules.PlumeIntervalFrames} sim " +
                             $"frames, lease {SnowGunPlumeRules.PlumeLeaseFrames} frames; Options 'Snow-gun plumes' " +
                             $"{(ReadSetting() ? "on" : "off")}; effect cap {SnowGunPlumeRules.PlumeMaxCount} on screen");
            }
            catch (Exception e)
            {
                Fail(e, "during load setup");
            }
        }

        protected override void OnUpdate()
        {
            if (m_Disabled || !m_Armed || !ResortPresence.Present)
                return;
            try
            {
                Pass();
            }
            catch (Exception e)
            {
                Fail(e, "");
            }
        }

        private static bool ReadSetting()
        {
            return Mod.Settings?.SnowGunPlumes ?? SkiResortMod.Settings.SkiResortSettings.DefaultSnowGunPlumes;
        }

        private void Pass()
        {
            bool setting = ReadSetting();
            uint frame = m_Simulation.frameIndex;
            float thresholdC = m_SnowLine.GetFreezingTemperature() + m_SnowLine.GetSnowmakingMarginC();
            // L2: the lapse read ONCE per pass (SnowLineSystem.GetLocalTemperature = ambient - (y - base) * scale; the
            // same expression below, so each gun's value is bit-identical to the per-gun call it replaces).
            m_SnowLine.GetLocalTemperatureCoefficients(out float lapseAmbient, out float lapseBase, out float lapseScale);
            m_Write.Clear();
            m_WriteEnd.Clear();
            m_WriteFlip.Clear();
            int total = 0;

            var runs = m_RunQuery.ToEntityArray(Allocator.Temp);
            try
            {
                for (int r = 0; r < runs.Length; r++)
                {
                    Entity run = runs[r];
                    if (!m_RunPrefabs.Contains(EntityManager.GetComponentData<PrefabRef>(run).m_Prefab))
                        continue;
                    m_Injection.TryGetRunGunsRunning(run, out bool running);
                    m_Subs.Clear();
                    var buffer = EntityManager.GetBuffer<Game.Objects.SubObject>(run, isReadOnly: true);
                    for (int i = 0; i < buffer.Length; i++)
                        m_Subs.Add(buffer[i].m_SubObject);

                    int on = 0, guns = 0;
                    for (int i = 0; i < m_Subs.Count; i++)
                    {
                        Entity gun = m_Subs[i];
                        if (!EntityManager.Exists(gun) || !EntityManager.HasComponent<PrefabRef>(gun) ||
                            !m_GunPrefabs.Contains(EntityManager.GetComponentData<PrefabRef>(gun).m_Prefab) ||
                            EntityManager.HasComponent<Deleted>(gun) || EntityManager.HasComponent<Temp>(gun) ||
                            !EntityManager.HasComponent<Game.Objects.Secondary>(gun) ||
                            !EntityManager.HasComponent<Owner>(gun) || EntityManager.GetComponentData<Owner>(gun).m_Owner != run ||
                            !EntityManager.HasComponent<Game.Objects.Transform>(gun))
                            continue;
                        guns++;
                        float gunC = lapseAmbient -
                                     (EntityManager.GetComponentData<Game.Objects.Transform>(gun).m_Position.y - lapseBase) * lapseScale;
                        bool want = SnowGunPlumeRules.PlumeOn(setting, running, gunC, thresholdC);
                        if (want)
                            on++;
                        bool has = EntityManager.HasComponent<Game.Buildings.EarlyDisasterWarningDuration>(gun);
                        uint end = has ? EntityManager.GetComponentData<Game.Buildings.EarlyDisasterWarningDuration>(gun).m_EndFrame : 0u;
                        if (!SnowGunPlumeRules.NeedsWrite(want, has, end, frame))
                            continue;
                        m_Write.Add(gun);
                        m_WriteEnd.Add(want ? SnowGunPlumeRules.LeaseEnd(frame) : 0u);
                        m_WriteFlip.Add(SnowGunPlumeRules.Flips(want, has, end, frame));
                    }
                    total += on;
                    NoteRun(run, on, guns, running);
                }
            }
            finally
            {
                runs.Dispose();
            }

            // Mutate after collecting (structural changes invalidate the chunks the buffers live in).
            int flips = 0;
            for (int i = 0; i < m_Write.Count; i++)
            {
                Entity gun = m_Write[i];
                var lease = new Game.Buildings.EarlyDisasterWarningDuration { m_EndFrame = m_WriteEnd[i] };
                if (EntityManager.HasComponent<Game.Buildings.EarlyDisasterWarningDuration>(gun))
                    EntityManager.SetComponentData(gun, lease);
                else
                    EntityManager.AddComponentData(gun, lease);
                if (m_WriteFlip[i])
                {
                    flips++;
                    if (!EntityManager.HasComponent<EffectsUpdated>(gun))
                        EntityManager.AddComponent<EffectsUpdated>(gun);
                }
            }

            if (total > SnowGunPlumeRules.PlumeMaxCount && !m_CapWarned)
            {
                m_CapWarned = true;
                Mod.Log.Warn($"Plume: WARN — {total} gun(s) want a plume, more than the effect's cap of " +
                             $"{SnowGunPlumeRules.PlumeMaxCount}: with all of them on screen the game shows the first " +
                             $"{SnowGunPlumeRules.PlumeMaxCount} and drops the rest silently (once per load)");
            }
            Census(total, flips);
        }

        /// <summary>One line when a run's plumes go from none to some or back (first time per run per load included).</summary>
        private void NoteRun(Entity run, int on, int guns, bool running)
        {
            bool known = m_RunPlumes.TryGetValue(run, out int before);
            m_RunPlumes[run] = on;
            if (known && (before > 0) == (on > 0))
                return;
            if (!known && on == 0 && guns == 0)
                return;   // a run with no plume gun: nothing to say
            Mod.Log.Info($"Plume: run {run.Index}:{run.Version} " + (on > 0
                ? $"ON — {on} of {guns} gun(s) plume (guns running; the rest are above the snowmaking temperature)"
                : $"OFF — 0 of {guns} gun(s) plume (" + (running ? "too warm at every gun" : "guns not running: warm, pack at " +
                  $"the stop depth, unbound or no measurement yet") + (ReadSetting() ? "" : "; Options 'Snow-gun plumes' off") + ")"));
        }

        /// <summary>The plume count, when it changed, at most once per CensusWindowS wall-clock seconds (the last value wins).</summary>
        private void Census(int total, int flips)
        {
            if (total != m_LastTotal)
                m_PendingTotal = total;
            if (m_PendingTotal < 0)
                return;
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (now - m_LastCensusAt < CensusWindowS)
                return;
            m_LastCensusAt = now;
            Mod.Log.Info($"Plume: census — {m_PendingTotal} gun(s) plume over {m_RunPlumes.Count} run(s) " +
                         $"(was {(m_LastTotal < 0 ? "none counted" : m_LastTotal.ToString())}; {flips} flip(s) this pass)");
            m_LastTotal = m_PendingTotal;
            m_PendingTotal = -1;
        }

        private bool HasPlumeEntry(Entity gunPrefab)
        {
            if (m_PlumeEffect == Entity.Null || !EntityManager.HasBuffer<Effect>(gunPrefab))
                return false;
            var effects = EntityManager.GetBuffer<Effect>(gunPrefab, isReadOnly: true);
            for (int i = 0; i < effects.Length; i++)
            {
                if (effects[i].m_Effect == m_PlumeEffect)
                    return true;
            }
            return false;
        }

        // House pattern: fast PrefabID path + name-scan fallback, tolerate missing. Session cache.
        private void Resolve()
        {
            if (m_Resolved)
                return;
            m_Resolved = true;
            foreach (string name in RunSurfaceNames)
            {
                Entity e = ResolvePrefab("SurfacePrefab", name);
                if (e == Entity.Null)
                    Mod.Log.Info($"Plume: run surface prefab '{name}' not found (tolerated)");
                else
                    m_RunPrefabs.Add(e);
            }
            foreach (string name in GunNames)
            {
                Entity e = ResolvePrefab("StaticObjectPrefab", name);
                if (e == Entity.Null)
                    Mod.Log.Info($"Plume: gun prefab '{name}' not found (tolerated)");
                else
                    m_GunPrefabs.Add(e);
            }
            m_PlumeEffect = ResolvePrefab("EffectPrefab", PlumeEffectName);
        }

        private Entity ResolvePrefab(string prefabType, string name)
        {
            Entity prefabEntity = Entity.Null;
            if (m_PrefabSystem.TryGetPrefab(new PrefabID(prefabType, name), out PrefabBase prefabBase))
                m_PrefabSystem.TryGetEntity(prefabBase, out prefabEntity);
            if (prefabEntity != Entity.Null)
                return prefabEntity;
            var candidates = m_PrefabScanQuery.ToEntityArray(Allocator.Temp);
            try
            {
                for (int i = 0; i < candidates.Length; i++)
                {
                    if (m_PrefabSystem.GetPrefabName(candidates[i]) == name)
                        return candidates[i];
                }
            }
            finally
            {
                candidates.Dispose();
            }
            return Entity.Null;
        }

        private void Fail(Exception e, string where)
        {
            m_Disabled = true;
            if (m_ErrorLogged)
                return;
            m_ErrorLogged = true;
            Mod.Log.Error($"!!! {nameof(SkiSnowGunPlumeSystem)} EXCEPTION {where} — snow-gun plumes frozen until the next game " +
                          $"load (a plume's lease ends {SnowGunPlumeRules.PlumeLeaseFrames} sim frames after its last renewal; " +
                          $"the game drops it at its next re-check). Please report this stack:\n{e}");
        }
    }
}
