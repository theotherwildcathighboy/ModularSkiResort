// SkiNightLightSystem — makes Night-gated lights on our run props actually FOLLOW the
// day/night cycle (DECISIONS 81/83/84, docs/research/light-refresh-mechanism.md).
//
// WHY THIS EXISTS — the mechanism, field-proven by the control-mast test:
// EffectConditionFlags.Night (8192) evaluates CORRECTLY on a LightEffect, but nothing in
// the engine ever re-evaluates it on a plain static area-border sub-object. Every write
// site of the re-evaluation tags was searched (light-refresh-mechanism.md §1): on our
// props, in Game mode, nothing stamps them on a cadence. The user's field test showed the
// full picture in one session: a Night-gated light loaded at night stayed ON all the next
// day — until a tool hover corrected it. The hover mechanism, VERIFIED (not a rebuild):
// hover adds Hidden to the original area (GenerateAreasSystem.cs:172-174) and
// SubObjectHiddenSystem cascades Hidden + BatchesUpdated onto the border props
// (:177-196), which forces the light OFF via the Hidden gate (EffectControlData.cs:669);
// un-hover removes Hidden and stamps BatchesUpdated again (ToolClearSystem.cs:96-106),
// which re-enters EffectControlSystem's m_UpdatedEffectsQuery — the SAME Any-list
// (Updated, Deleted, EffectsUpdated, BatchesUpdated; EffectControlSystem.cs:1087-1097)
// this system feeds — and the Night condition is re-tested fresh. No sub-object is
// deleted or recreated. Vanilla street lights only work because StreetLightSystem
// explicitly stamps EffectsUpdated at dusk and dawn (StreetLightSystem.cs:204, :218,
// :236). This system is our StreetLightSystem.
//
// Stamping also closes the FLY-BY HAZARD (entry 83): Game.Effects.SearchSystem's
// distance path re-enables an out-of-range light on approach WITHOUT re-testing its
// conditions (ActionFlags.SkipEnabled, checkEnabled: false at EffectControlSystem.cs:700)
// — from tree membership as stale as the enabled state. A wrongly-on light survives at
// most one flag pass here before the next stamp corrects it.
//
// REGISTRATION IS LOAD-BEARING TWICE (the entry-79 lesson, applied again):
//   * ANCHOR: UpdateAfter<EffectFlagSystem>(GameSimulation) — we read the very flag
//     vanilla just wrote (EffectFlagSystem.OnUpdate:155 sets m_IsNightTime from
//     TimeSystem.normalizedTime vs kNightBegin 0.75 / kDayBegin), via the PUBLIC
//     GetData(). One clock, never a reimplementation drifting from it.
//   * INTERVAL: GetUpdateInterval MUST return EffectFlagSystem's own 2048 — UpdateSystem
//     inherits an anchor's runtime offset only when the intervals are EQUAL (the
//     equality test lives at UpdateSystem.cs:406/:431, re-derived in PatchSystemOffset
//     :457-517; verified). An unequal interval does NOT drift — it gets a fixed
//     scatter-assigned offset that either aligns with the anchor or NEVER coincides,
//     and which alignment you get can silently flip whenever the full system
//     registration set changes (e.g. another mod adds a system). Equality is what makes
//     same-frame-after-anchor a guarantee instead of luck.
//   * TAG LIFETIME: a tag stamped here (GameSimulation, LateUpdate) survives into the
//     NEXT frame's PreCulling, where EffectControlSystem (SystemOrder.cs:636) consumes
//     it, and dies at that frame's CleanUpSystem. The window is exactly wide enough.
//
// THE SETTLE WINDOW — patch robustness, not superstition: IsEnabled staggers Night
// on-switching by up to kNightRandomTicks (15000) past m_LastTimeChange
// (EffectFlagSystem.cs:66). Today that stagger is DEAD CODE — OnUpdate never assigns
// m_LastTimeChange for the time branch (entry 83; reads like a vanilla oversight), so the
// flag collapses to m_IsNightTime and ONE stamp would do. But if a patch fixes the
// assignment, a single stamp AT the flip would evaluate inside the stagger window and
// freeze most lights OFF until something re-stamped them. So after every flip we re-stamp
// for SettlePasses passes: 8 x 2048 = 16384 > 15000 covers the full stagger, and in the
// current (bugged) engine the extra stamps are harmless no-ops.
//
// SCOPE: border sub-objects owned by OUR run areas only (Secondary + Owner, owner's
// prefab in the watched set). Stamping a prop with no effects is a no-op; the per-pass
// cost is a few dozen AddComponent calls, twice per game day plus settle.

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
            /// <summary>Passes to keep re-stamping after a day/night flip. 8 x 2048 =
            /// 16384 sim frames, covering EffectFlagSystem's full 15000-tick stagger
            /// window should a patch ever revive it (see header).</summary>
            public const int SettlePasses = 8;
        }

        // MUST equal EffectFlagSystem.GetUpdateInterval (2048) or the anchored offset is
        // not inherited and we stop running right after the flag write.
        private const int FlagInterval = 2048;

        // SkiRunNightA is watched ahead of its existence (tolerated-missing, the house
        // pattern) so the night-skiing surface lights up the moment it ships.
        // E1 (D16): SkiRunNaturalA is deliberately absent — a natural run has no floodlights.
        private static readonly string[] RunSurfaceNames =
            { "SkiRunSnowmakingA", "SkiRunGroomedA", "SkiRunNightA" };

        // T22: the three floodlight mast prefabs (StaticObjectPrefab; DECISIONS 81). Their lamps shine along local +Z with a
        // 25 deg downward pitch baked in the EffectSource rotation; the game places them with +Z along the border, so
        // AimMasts turns them to face into the run (MastAimRules, RunPropDensityRules.cs).
        private static readonly string[] MastNames =
            { "SkiPisteFloodlight", "SkiPisteFloodlightLow", "SkiPisteFloodlightTall" };

        private EffectFlagSystem m_EffectFlagSystem;
        private PrefabSystem m_PrefabSystem;
        private EntityQuery m_PropQuery;
        private EntityQuery m_PrefabScanQuery;

        private readonly HashSet<Entity> m_RunPrefabs = new HashSet<Entity>();
        private readonly HashSet<Entity> m_MastPrefabs = new HashSet<Entity>();
        private bool m_PrefabsResolved;
        // AimMasts scratch (a ring copied per mast owner; masts are few, rings are tens of nodes).
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

            m_PropQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Secondary>(),
                    ComponentType.ReadOnly<Owner>(),
                    ComponentType.ReadOnly<PrefabRef>(),
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

        /// <summary>MUST match EffectFlagSystem's 2048 — see header.</summary>
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
                // ONE line per load for the three run-visual features (T22 / T30 / T36, field test 2026-10-15).
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

                // T22: face the masts into the run. Cheap when nothing is out of aim (a prefab test per prop).
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

                // First pass after load stamps once as hygiene: the fly-by hazard means a
                // save can carry a wrongly-enabled light, and one stamp heals it.
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

        private int StampRunProps()
        {
            var toStamp = new NativeList<Entity>(64, Allocator.Temp);
            var chunks = m_PropQuery.ToArchetypeChunkArray(Allocator.Temp);
            try
            {
                var entityHandle = GetEntityTypeHandle();
                var ownerHandle = GetComponentTypeHandle<Owner>(isReadOnly: true);
                for (int c = 0; c < chunks.Length; c++)
                {
                    var entities = chunks[c].GetNativeArray(entityHandle);
                    var owners = chunks[c].GetNativeArray(ref ownerHandle);
                    for (int i = 0; i < owners.Length; i++)
                    {
                        Entity owner = owners[i].m_Owner;
                        if (!EntityManager.HasComponent<PrefabRef>(owner))
                            continue;
                        Entity ownerPrefab =
                            EntityManager.GetComponentData<PrefabRef>(owner).m_Prefab;
                        if (m_RunPrefabs.Contains(ownerPrefab))
                            toStamp.Add(entities[i]);
                    }
                }
            }
            finally
            {
                chunks.Dispose();
            }

            for (int i = 0; i < toStamp.Length; i++)
                EntityManager.AddComponent<EffectsUpdated>(toStamp[i]);
            int n = toStamp.Length;
            toStamp.Dispose();
            return n;
        }

        // House pattern: fast PrefabID path + name-scan fallback, tolerate missing.
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

        /// <summary>T22 (item 87): turns every floodlight mast of our run areas to face INTO the run, leaning toward
        /// downhill (MastAimRules). The game places a border prop with +Z along the outgoing border segment and ignores the
        /// winding (SecondaryObjectSystem.cs:2984-2992), so the lamps shine along the border; the prefab cannot carry the
        /// fix (the run's two sides mirror). Same write the game itself makes when it moves a sub-object: SetComponent
        /// Transform + Updated (SecondaryObjectSystem.cs:3414-3420). Position never changes, so placement and the density
        /// slider's seeded picks are untouched; an owner update makes the game put the border-aligned rotation back, and
        /// the next pass (every FlagInterval frames) turns the mast again. A mast already within WriteToleranceDeg of the
        /// aim is not written. Returns the number turned; unmatched = masts with no ring node within MatchRadiusM.</summary>
        private int AimMasts(out int unmatched, out int total)
        {
            unmatched = 0;
            total = 0;
            if (m_MastPrefabs.Count == 0)
                return 0;
            var toAim = new NativeList<Entity>(16, Allocator.Temp);
            var toRot = new NativeList<quaternion>(16, Allocator.Temp);
            var chunks = m_PropQuery.ToArchetypeChunkArray(Allocator.Temp);
            try
            {
                var entityHandle = GetEntityTypeHandle();
                var ownerHandle = GetComponentTypeHandle<Owner>(isReadOnly: true);
                var prefabHandle = GetComponentTypeHandle<PrefabRef>(isReadOnly: true);
                for (int c = 0; c < chunks.Length; c++)
                {
                    var entities = chunks[c].GetNativeArray(entityHandle);
                    var owners = chunks[c].GetNativeArray(ref ownerHandle);
                    var prefabs = chunks[c].GetNativeArray(ref prefabHandle);
                    for (int i = 0; i < entities.Length; i++)
                    {
                        if (!m_MastPrefabs.Contains(prefabs[i].m_Prefab))
                            continue;
                        Entity owner = owners[i].m_Owner;
                        if (!EntityManager.HasComponent<PrefabRef>(owner) ||
                            !m_RunPrefabs.Contains(EntityManager.GetComponentData<PrefabRef>(owner).m_Prefab) ||
                            !EntityManager.HasBuffer<Game.Areas.Node>(owner))
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
                        Game.Objects.Transform t = EntityManager.GetComponentData<Game.Objects.Transform>(entities[i]);
                        int node = MastAimRules.NearestNode(m_RingX, m_RingZ, n, t.m_Position.x, t.m_Position.z);
                        if (node < 0 || !MastAimRules.Aim(m_RingX, m_RingY, m_RingZ, n, node, out float ax, out float az))
                        {
                            unmatched++;
                            continue;
                        }
                        float3 fwd = math.mul(t.m_Rotation, new float3(0f, 0f, 1f));
                        if (MastAimRules.AngleDeg(fwd.x, fwd.z, ax, az) < MastAimRules.WriteToleranceDeg)
                            continue;
                        toAim.Add(entities[i]);
                        toRot.Add(quaternion.LookRotationSafe(new float3(ax, 0f, az), math.up()));
                    }
                }
            }
            finally
            {
                chunks.Dispose();
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
