// Makes Night-gated lights on our run props follow the day/night cycle.
// Why: nothing in the engine re-evaluates EffectConditionFlags.Night on a static area-border sub-object, so a light loaded at night
// stays ON next day until a tool hover; this system stamps the re-evaluation tags at each flip (our StreetLightSystem).
// Registration is load-bearing: UpdateAfter<EffectFlagSystem>(GameSimulation), and GetUpdateInterval must equal EffectFlagSystem's
// 2048 (UpdateSystem inherits an anchor's runtime offset only for EQUAL intervals, UpdateSystem.cs:406/:431).
// Scope: border sub-objects owned by our run areas only; a city with no resort or no run area returns before any query result is read.
// Engine notes: EngineNotes/SkiNightLightSystem.md

using System;
using System.Collections.Generic;
using Colossal.Serialization.Entities;
using Game;
using Game.Common;
using Game.Effects;
using Game.Objects;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public sealed partial class SkiNightLightSystem : GameSystemBase
    {
        private static class Tuning
        {
            /// <summary>Passes to keep re-stamping after a day/night flip: 8 x 2048 = 16384 sim frames, covering EffectFlagSystem's
            /// 15000-tick stagger should a patch revive it.</summary>
            public const int SettlePasses = 8;
        }

        // Must equal EffectFlagSystem.GetUpdateInterval (2048) or the anchored offset is not inherited
        private const int FlagInterval = 2048;

        // SkiRunNightA is watched ahead of its existence (tolerated-missing) so the night-skiing surface lights up the moment it
        // ships. SkiRunNaturalA is absent: a natural run has no floodlights.
        private static readonly string[] RunSurfaceNames =
            { "SkiRunSnowmakingA", "SkiRunGroomedA", "SkiRunNightA" };

        // The three floodlight mast prefabs (StaticObjectPrefab). Their lamps shine along local +Z with a 25 deg downward pitch baked
        // in the EffectSource rotation; the game places them with +Z along the border, so AimMasts turns them to face into the run
        // (MastAimRules, RunPropDensityRules.cs).
        private static readonly string[] MastNames =
            { "SkiPisteFloodlight", "SkiPisteFloodlightLow", "SkiPisteFloodlightTall" };

        private EffectFlagSystem m_EffectFlagSystem;
        private PrefabSystem m_PrefabSystem;
        private EntityQuery m_RunQuery;
        private EntityQuery m_PrefabScanQuery;

        private readonly HashSet<Entity> m_RunPrefabs = new HashSet<Entity>();
        private readonly HashSet<Entity> m_MastPrefabs = new HashSet<Entity>();
        private bool m_PrefabsResolved;
        // AimMasts scratch: a ring copied per mast owner (masts are few, rings are tens of nodes)
        private float[] m_RingX = new float[64], m_RingY = new float[64], m_RingZ = new float[64];
        private int m_AimedTotal;

        private bool m_Armed;
        private bool m_Disabled;
        private bool m_ErrorLogged;
        private bool m_HaveFlag;
        private bool m_LastNight;
        private int m_SettleRemaining;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_EffectFlagSystem = World.GetOrCreateSystemManaged<EffectFlagSystem>();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();

            m_RunQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Game.Areas.Area>(),
                    ComponentType.ReadOnly<Game.Areas.Surface>(),   // SurfacePrefab.cs:21-23: our runs, not lots / districts / tiles
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

            Mod.Log.Info($"{nameof(SkiNightLightSystem)} created");
        }

        /// <summary>Must match EffectFlagSystem's 2048 (see engine notes).</summary>
        public override int GetUpdateInterval(SystemUpdatePhase phase)
        {
            return FlagInterval;
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);

            m_Armed = false;
            m_Disabled = false;
            m_ErrorLogged = false;
            m_HaveFlag = false;
            m_SettleRemaining = 0;
            m_AimedTotal = 0;

            if (mode != GameMode.Game)
                return;

            try
            {
                ResolveRunPrefabs();
                // One line per load for the three run-visual features
                Mod.Log.Info($"RunVisuals: ARMED - (T22) night masts turn to face into the run with up to " +
                             $"{MastAimRules.BiasMaxDeg:0} deg downhill lean, {m_MastPrefabs.Count} of {MastNames.Length} mast " +
                             $"prefab(s), checked every {FlagInterval} frames (lamp pitch stays the prefab's 25 deg); (T30) glade " +
                             $"tool outlines {HexOf(RunOutlineRules.GladeOutlineColour)} {RunOutlineRules.GladeLineWidthM:0.0#} m, " +
                             $"dashed {RunOutlineRules.GladeDashM:0}/{RunOutlineRules.GladeGapM:0} m on bound runs, solid when " +
                             $"hovered; (T36) run corner dots opaque white with a dark rim at Options 'run outline size' (0 = the " +
                             "game's own dots)");
                if (m_RunPrefabs.Count == 0)
                {
                    Mod.Log.Info("NightLight: no run surface prefab present — light refresh " +
                                 "inert until one is imported (expected pre-import)");
                    return;
                }
                m_Armed = true;
                Mod.Log.Info("NightLight: ARMED — stamping EffectsUpdated on run-owned props " +
                             "at every day/night flip (+ settle x" +
                             $"{Tuning.SettlePasses}), anchored after EffectFlagSystem @ " +
                             $"{FlagInterval} frames; nothing else re-evaluates Night-gated " +
                             "lights on area sub-objects (light-refresh-mechanism.md)");
            }
            catch (Exception e)
            {
                m_Disabled = true;
                if (!m_ErrorLogged)
                {
                    m_ErrorLogged = true;
                    Mod.Log.Error($"!!! {nameof(SkiNightLightSystem)} EXCEPTION during load " +
                                  "setup — night-light refresh disabled until the next game " +
                                  $"load. Please report this stack:\n{e}");
                }
            }
        }

        protected override void OnUpdate()
        {
            try
            {
                if (m_Disabled || !m_Armed)
                    return;
                // No sign, run or lift of ours (ResortPresence) or no run area at all: nothing to aim or stamp. m_HaveFlag stays false, so the
                // first pass after a resort appears is the hygiene pass.
                if (!ResortPresence.Present || m_RunQuery.IsEmptyIgnoreFilter)
                    return;

                // Face the masts into the run; cheap when nothing is out of aim (a prefab test per prop)
                int aimed = AimMasts(out int aimUnmatched, out int aimTotal);
                if (aimed > 0 || (!m_HaveFlag && aimTotal > 0))
                {
                    m_AimedTotal += aimed;
                    Mod.Log.Info($"NightLight: aimed {aimed} of {aimTotal} mast(s) into their run" +
                                 (aimUnmatched > 0 ? $" ({aimUnmatched} not matched to a ring node, left as placed)" : "") +
                                 $" - {m_AimedTotal} this load");
                }

                bool night = m_EffectFlagSystem.GetData().m_IsNightTime;

                bool flip = m_HaveFlag && night != m_LastNight;
                bool firstPass = !m_HaveFlag;
                m_HaveFlag = true;
                m_LastNight = night;

                if (flip)
                    m_SettleRemaining = Tuning.SettlePasses;

                // First pass after load stamps once as hygiene: the fly-by hazard means a save can carry a wrongly-enabled light.
                if (!flip && !firstPass && m_SettleRemaining <= 0)
                    return;
                if (m_SettleRemaining > 0)
                    m_SettleRemaining--;

                int stamped = StampRunProps();
                if (flip || firstPass)
                {
                    Mod.Log.Info($"NightLight: {(night ? "-> NIGHT" : "-> DAY")}" +
                                 (firstPass ? " (initial hygiene pass)" : "") +
                                 $" — EffectsUpdated stamped on {stamped} run prop(s)" +
                                 (stamped == 0 ? " (none present — nothing to refresh)" : ""));
                }
            }
            catch (Exception e)
            {
                m_Disabled = true;
                if (!m_ErrorLogged)
                {
                    m_ErrorLogged = true;
                    Mod.Log.Error($"!!! {nameof(SkiNightLightSystem)} EXCEPTION — night-light " +
                                  "refresh disabled until the next game load. Please report " +
                                  $"this stack:\n{e}");
                }
            }
        }

        /// <summary>The live Secondary props of our run areas (their SubObject buffers), optionally only those whose prefab is in
        /// <paramref name="onlyPrefabs"/> (the masts). Appends (prop, run) pairs.</summary>
        private void CollectRunProps(NativeList<Entity> props, NativeList<Entity> owners, HashSet<Entity> onlyPrefabs)
        {
            var runs = m_RunQuery.ToEntityArray(Allocator.Temp);
            try
            {
                for (int r = 0; r < runs.Length; r++)
                {
                    Entity run = runs[r];
                    if (!m_RunPrefabs.Contains(EntityManager.GetComponentData<PrefabRef>(run).m_Prefab))
                        continue;
                    var buffer = EntityManager.GetBuffer<Game.Objects.SubObject>(run, isReadOnly: true);
                    for (int i = 0; i < buffer.Length; i++)
                    {
                        Entity prop = buffer[i].m_SubObject;
                        if (!EntityManager.Exists(prop) || !EntityManager.HasComponent<Secondary>(prop) ||
                            EntityManager.HasComponent<Deleted>(prop) || EntityManager.HasComponent<Temp>(prop) ||
                            !EntityManager.HasComponent<Owner>(prop) ||
                            EntityManager.GetComponentData<Owner>(prop).m_Owner != run)
                            continue;
                        if (onlyPrefabs != null &&
                            (!EntityManager.HasComponent<PrefabRef>(prop) ||
                             !onlyPrefabs.Contains(EntityManager.GetComponentData<PrefabRef>(prop).m_Prefab)))
                            continue;
                        props.Add(prop);
                        if (owners.IsCreated)
                            owners.Add(run);
                    }
                }
            }
            finally
            {
                runs.Dispose();
            }
        }

        private int StampRunProps()
        {
            var toStamp = new NativeList<Entity>(64, Allocator.Temp);
            try
            {
                CollectRunProps(toStamp, default, null);
                if (toStamp.Length > 0)
                    EntityManager.AddComponent<EffectsUpdated>(toStamp.AsArray());   // one structural change, not one per prop
                return toStamp.Length;
            }
            finally
            {
                toStamp.Dispose();
            }
        }

        private void ResolveRunPrefabs()
        {
            if (m_PrefabsResolved)
                return;
            m_PrefabsResolved = true;

            foreach (string name in RunSurfaceNames)
            {
                Entity prefabEntity = ResolvePrefab("SurfacePrefab", name);
                if (prefabEntity == Entity.Null)
                {
                    Mod.Log.Info($"NightLight: run surface prefab '{name}' not found (tolerated)");
                    continue;
                }
                m_RunPrefabs.Add(prefabEntity);
            }
            foreach (string name in MastNames)
            {
                Entity prefabEntity = ResolvePrefab("StaticObjectPrefab", name);
                if (prefabEntity == Entity.Null)
                {
                    Mod.Log.Info($"NightLight: mast prefab '{name}' not found (tolerated; its lamps keep the border-aligned aim)");
                    continue;
                }
                m_MastPrefabs.Add(prefabEntity);
            }
        }

        private static string HexOf(float4 c)
        {
            return "#" + ((int)(c.x * 255f + 0.5f)).ToString("X2") + ((int)(c.y * 255f + 0.5f)).ToString("X2") +
                   ((int)(c.z * 255f + 0.5f)).ToString("X2") + " a" + c.w.ToString("0.##");
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

        /// <summary>Turns every floodlight mast of our run areas to face into the run, leaning toward downhill (MastAimRules). The game
        /// places a border prop with +Z along the outgoing border segment and ignores the winding (SecondaryObjectSystem.cs:2984-2992),
        /// and the prefab cannot carry the fix (the run's two sides mirror). Same write the game makes when it moves a sub-object:
        /// SetComponent Transform + Updated (SecondaryObjectSystem.cs:3414-3420). Position never changes, so placement and the density
        /// slider's seeded picks are untouched; an owner update makes the game put the border-aligned rotation back and the next pass
        /// turns the mast again. A mast already within WriteToleranceDeg of the aim is not written. Returns the number turned;
        /// unmatched = masts with no ring node within MatchRadiusM.</summary>
        private int AimMasts(out int unmatched, out int total)
        {
            unmatched = 0;
            total = 0;
            if (m_MastPrefabs.Count == 0)
                return 0;
            var toAim = new NativeList<Entity>(16, Allocator.Temp);
            var toRot = new NativeList<quaternion>(16, Allocator.Temp);
            var masts = new NativeList<Entity>(16, Allocator.Temp);
            var mastRuns = new NativeList<Entity>(16, Allocator.Temp);
            try
            {
                CollectRunProps(masts, mastRuns, m_MastPrefabs);
                {
                    for (int m = 0; m < masts.Length; m++)
                    {
                        Entity mast = masts[m];
                        Entity owner = mastRuns[m];
                        if (!EntityManager.HasBuffer<Game.Areas.Node>(owner))
                            continue;
                        total++;
                        DynamicBuffer<Game.Areas.Node> ring = EntityManager.GetBuffer<Game.Areas.Node>(owner, true);
                        int n = ring.Length;
                        if (n < 3)
                        {
                            unmatched++;
                            continue;
                        }
                        if (m_RingX.Length < n)
                        {
                            m_RingX = new float[n * 2];
                            m_RingY = new float[n * 2];
                            m_RingZ = new float[n * 2];
                        }
                        for (int k = 0; k < n; k++)
                        {
                            float3 p = ring[k].m_Position;
                            m_RingX[k] = p.x;
                            m_RingY[k] = p.y;
                            m_RingZ[k] = p.z;
                        }
                        Game.Objects.Transform t = EntityManager.GetComponentData<Game.Objects.Transform>(mast);
                        int node = MastAimRules.NearestNode(m_RingX, m_RingZ, n, t.m_Position.x, t.m_Position.z);
                        if (node < 0 || !MastAimRules.Aim(m_RingX, m_RingY, m_RingZ, n, node, out float ax, out float az))
                        {
                            unmatched++;
                            continue;
                        }
                        float3 fwd = math.mul(t.m_Rotation, new float3(0f, 0f, 1f));
                        if (MastAimRules.AngleDeg(fwd.x, fwd.z, ax, az) < MastAimRules.WriteToleranceDeg)
                            continue;
                        toAim.Add(mast);
                        toRot.Add(quaternion.LookRotationSafe(new float3(ax, 0f, az), math.up()));
                    }
                }
            }
            finally
            {
                masts.Dispose();
                mastRuns.Dispose();
            }

            for (int i = 0; i < toAim.Length; i++)
            {
                Game.Objects.Transform t = EntityManager.GetComponentData<Game.Objects.Transform>(toAim[i]);
                t.m_Rotation = toRot[i];
                EntityManager.SetComponentData(toAim[i], t);
                if (!EntityManager.HasComponent<Updated>(toAim[i]))
                    EntityManager.AddComponent<Updated>(toAim[i]);
            }
            int n2 = toAim.Length;
            toAim.Dispose();
            toRot.Dispose();
            return n2;
        }
    }
}
