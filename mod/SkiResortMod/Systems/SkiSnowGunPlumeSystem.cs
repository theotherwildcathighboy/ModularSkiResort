// Snow guns show a vapor plume while their run's guns are running (EffectSource entry on the gun prefabs, driven by a lease).
// Rule (one predicate with the injection, EngineNotes/SnowGunPlumeRules.md): Options toggle on, the run's guns ran in the injection's last
// pass (a cat track: its guns can run, no injection there), and the gun's own elevation is cold. FanIdle, hydrants and the folded lance carry no
// plume entry and are never touched.
// Scope: Game mode only; the guns the placer tagged with SkiRunPropMember (groomed runs with their guns on and cat tracks).
// Cadence: every PlumeIntervalFrames (64) sim frames, after the injection (Mod.cs; not load-bearing: reads per-run flags).
// Logs (silence must be impossible): ARMED / inert line per load, per-run start/stop lines, census on change, WARN once per load
// when guns exceed the effect's m_MaxCount, and the house one-shot "!!!" catch.
// Engine notes: EngineNotes/SkiSnowGunPlumeSystem.md

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
        // The run surfaces whose placed props can be guns (every groomed run with its guns on, DECISIONS 577).
        private static readonly string[] RunSurfaceNames = { SkiCatalog.Names.RunGroomed, "SkiRunSnowmakingA", "SkiRunNightA", SkiCatalog.Names.RunCatTrack };

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
        private SkiRunCatalogSystem m_Catalog;
        private EntityQuery m_MemberQuery;
        private EntityQuery m_PrefabScanQuery;

        private readonly HashSet<Entity> m_RunPrefabs = new HashSet<Entity>();
        private Entity m_CatTrackPrefab;
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
        private struct RunTally
        {
            public int On, Guns;
            public bool Running;
        }
        private readonly Dictionary<Entity, RunTally> m_Tally = new Dictionary<Entity, RunTally>();

        protected override void OnCreate()
        {
            base.OnCreate();
            m_Injection = World.GetOrCreateSystemManaged<SkiSnowInjectionSystem>();
            m_SnowLine = World.GetOrCreateSystemManaged<SnowLineSystem>();
            m_Simulation = World.GetOrCreateSystemManaged<SimulationSystem>();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_Catalog = World.GetOrCreateSystemManaged<SkiRunCatalogSystem>();
            m_MemberQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<SkiRunPropMember>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                    ComponentType.ReadOnly<Game.Objects.Transform>(),
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
                                  : m_RunPrefabs.Count == 0 ? "no groomed, snowmaking, night or cat track run prefab"
                                  : "no gun prefab carries the plume entry (prefab JSON not deployed?)") +
                                 $"; {m_GunPrefabs.Count} of {GunNames.Length} gun prefab(s) found");
                    return;
                }
                m_Armed = true;
                Mod.Log.Info($"Plume: ARMED — {withEntry} of {GunNames.Length} gun prefab(s) carry '{PlumeEffectName}' " +
                             $"({m_GunPrefabs.Count} found), {m_RunPrefabs.Count} run prefab(s) watched; a gun plumes while its " +
                             $"run's guns run (cold, pack below {RunSnowRules.GunStopDepthM:F2} m, restart below " +
                             $"{SnowGunPlumeRules.RestartBelowM:F2} m, natural snow counts) and the gun's own elevation is at or " +
                             $"below freezing + the snowmaking margin; a cat track's guns run while it is the gun season and part of the track is cold; checked every {SnowGunPlumeRules.PlumeIntervalFrames} sim " +
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
            // The lapse is read ONCE per pass (SnowLineSystem.GetLocalTemperature = ambient - (y - base) * scale); the same
            // expression below, so each gun's value is bit-identical to the per-gun call.
            m_SnowLine.GetLocalTemperatureCoefficients(out float lapseAmbient, out float lapseBase, out float lapseScale);
            m_Write.Clear();
            m_WriteEnd.Clear();
            m_WriteFlip.Clear();
            int total = 0;

            m_Tally.Clear();
            using (var members = m_MemberQuery.ToEntityArray(Allocator.Temp))
            using (var tags = m_MemberQuery.ToComponentDataArray<SkiRunPropMember>(Allocator.Temp))
            {
                for (int i = 0; i < members.Length; i++)
                {
                    if (tags[i].m_Class != RunPropDensityRules.ClassGun)
                        continue;
                    Entity gun = members[i];
                    if (!m_GunPrefabs.Contains(EntityManager.GetComponentData<PrefabRef>(gun).m_Prefab))
                        continue;
                    Entity run = tags[i].m_Run;
                    if (!m_Tally.TryGetValue(run, out RunTally tally))
                        tally = new RunTally { Running = RunGunsRunning(run) };
                    tally.Guns++;
                    float gunC = lapseAmbient -
                                 (EntityManager.GetComponentData<Game.Objects.Transform>(gun).m_Position.y - lapseBase) * lapseScale;
                    bool want = SnowGunPlumeRules.PlumeOn(setting, tally.Running, gunC, thresholdC);
                    if (want)
                        tally.On++;
                    m_Tally[run] = tally;
                    bool has = EntityManager.HasComponent<Game.Buildings.EarlyDisasterWarningDuration>(gun);
                    uint end = has ? EntityManager.GetComponentData<Game.Buildings.EarlyDisasterWarningDuration>(gun).m_EndFrame : 0u;
                    if (!SnowGunPlumeRules.NeedsWrite(want, has, end, frame))
                        continue;
                    m_Write.Add(gun);
                    m_WriteEnd.Add(want ? SnowGunPlumeRules.LeaseEnd(frame) : 0u);
                    m_WriteFlip.Add(SnowGunPlumeRules.Flips(want, has, end, frame));
                }
            }
            foreach (KeyValuePair<Entity, RunTally> pair in m_Tally)
            {
                total += pair.Value.On;
                NoteRun(pair.Key, pair.Value.On, pair.Value.Guns, pair.Value.Running);
            }

            // Mutate after collecting (structural changes invalidate the chunks).
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

        /// <summary>Whether this run's guns run now: the injection's flag, or on a cat track (no injection, no texels) the can-run rule.</summary>
        private bool RunGunsRunning(Entity run)
        {
            if (run == Entity.Null || !EntityManager.Exists(run) || !EntityManager.HasComponent<PrefabRef>(run))
                return false;
            if (EntityManager.GetComponentData<PrefabRef>(run).m_Prefab == m_CatTrackPrefab && m_CatTrackPrefab != Entity.Null)
                return m_Catalog.TryGetRow(run, out SkiRunCatalogSystem.RunRow row) && m_Injection.CatTrackGunsCanRun(run, row.NodeBottomM, row.NodeTopM);
            m_Injection.TryGetRunGunsRunning(run, out bool running);
            return running;
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
                {
                    Mod.Log.Info($"Plume: run surface prefab '{name}' not found (tolerated)");
                    continue;
                }
                m_RunPrefabs.Add(e);
                if (name == SkiCatalog.Names.RunCatTrack)
                    m_CatTrackPrefab = e;
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
