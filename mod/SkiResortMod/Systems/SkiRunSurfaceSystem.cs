// SkiRunSurfaceSystem — makes props standing on our ski runs whiten in step with the snow
// we inject, instead of being permanently summer-clean (docs/research/surface-snow-sync.md).
//
// THE BUG THIS FIXES IS BIGGER THAN THE FEATURE IT ADDS.
// Every imported static object's shader has a snow layer, fed per instance from
// `Game.Objects.Surface` via `colossal_Wetness`. An object with no Surface of its own walks
// up `Owner` to find one (BatchDataSystem.cs:801-816). Our snow guns are sub-objects of the
// run AREA — and `SurfacePrefab` adds `Game.Areas.Surface`, which is a ONE-BYTE EMPTY TAG
// (Game.Areas/Surface.cs:6-9), NOT the weather struct. Nothing in the assembly ever gives an
// area entity `Game.Objects.Surface`. So the owner walk finds nothing, finds no further
// Owner, and falls out with colossal_Wetness = (0,0,0,0): **props on our runs render
// snow-free and bone dry in every season, deep winter included.** The `WeatherObject`
// RequireSnow gate (SubObjectSystem.cs:3231) reads the same lookup, so snow-cap sub-objects
// could never fire either.
//
// The fix is to give the run area the component the engine is looking for, then keep it in
// step with the snow depth SkiSnowInjectionSystem already measures. Adding a runtime
// component to our own area entities is established practice here — SkiRunPropSystem
// already adds Game.Objects.SubObject and PseudoRandomSeed to these same entities.
//
// PHASE AND INTERVAL ARE BOTH LOAD-BEARING, for different reasons:
//   * PHASE/ANCHOR: `BatchDataSystem` only refreshes ObjectProperty.SurfaceWetness when
//     `m_WetnessSystem.LastSystemVersion` MOVES (GetDataQuery:2572, 2604-2609). A mod write
//     does not bump that version, so writing at any other cadence is either never read at
//     all, or read only after vanilla has already overwritten it. We must write in the same
//     tick vanilla does — hence UpdateAfter<WetnessSystem>.
//   * INTERVAL: UpdateSystem inherits an anchor's runtime offset ONLY WHEN THE INTERVALS
//     ARE EQUAL (UpdateSystem.cs:443-452). WetnessSystem is 256, so we are 256. At any
//     other value we would get our own distributed offset and might never land in
//     WetnessSystem's sim frame — the anchor would be decorative.
//
// WHY THE TUG-OF-WAR IS SAFE: WetnessSystem's query is type-only with no UpdateFrame
// slicing, and it applies one small speed-clamped step per pass (at +3 C it sheds ~7/255 of
// m_SnowAmount and ~0.7/255 of m_AccumulatedSnow). Running immediately after it in the same
// tick, our write dominates completely. Frame order helps too: GameSimulation runs in
// LateUpdate while the render consumer runs in the NEXT frame's Update, so our value is
// consumed once and then holds for the whole 256-frame window.
//
// WE OWN THE VALUE BOTH WAYS. This system SETS rather than clamps up: writing only upward
// would make the white wash immortal (~255 passes to clear at +3 C once vanilla is the only
// thing lowering it). Tracking our measured depth in both directions means run props go
// white as the ribbon builds and come clean as it melts — the same one-climate-model rule
// the rest of the mod follows.
//
// SCOPE: Game mode only. Only OUR run prefabs are touched; nothing else on the map is
// modified. m_Wetness and m_Dirtyness are left entirely to their vanilla owners.

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
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public sealed partial class SkiRunSurfaceSystem : GameSystemBase
    {
        private static class Tuning
        {
            /// <summary>Run snow depth (m) at which props read fully white. The snow shader
            /// saturates at cm scale, and the WeatherObject RequireSnow gate trips at
            /// m_AccumulatedSnow >= 15/255, so 5 cm gives a comfortable margin over both
            /// while still letting a thin early-season base read as "dusted".</summary>
            public const float FullWhiteDepthM = 0.05f;

            /// <summary>Log every Nth pass even when the value is unchanged (256 frames per
            /// pass, so 8 is roughly one line per two in-game hours at normal speed).</summary>
            public const int LogEveryNth = 8;
        }

        // Must match WetnessSystem's interval or the anchored offset is not inherited.
        private const int WetnessInterval = 256;

        private static readonly string[] RunSurfaceNames =
            { "SkiRunSnowmakingA", "SkiRunGroomedA", "SkiRunNightA" };

        private PrefabSystem m_PrefabSystem;
        private SkiSnowInjectionSystem m_SnowInjectionSystem;
        private EntityQuery m_RunAreaQuery;
        private EntityQuery m_PrefabScanQuery;

        private readonly HashSet<Entity> m_RunPrefabs = new HashSet<Entity>();
        private bool m_PrefabsResolved;

        private bool m_Armed;
        private bool m_Disabled;
        private bool m_ErrorLogged;
        private long m_PassCounter;
        private int m_LastByte = -1;
        private int m_LastAreaCount = -1;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_SnowInjectionSystem = World.GetOrCreateSystemManaged<SkiSnowInjectionSystem>();

            m_RunAreaQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Area>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Temp>(),
                },
            });
            m_PrefabScanQuery = GetEntityQuery(ComponentType.ReadOnly<PrefabData>());

            Mod.Log.Info($"{nameof(SkiRunSurfaceSystem)} created");
        }

        /// <summary>MUST equal WetnessSystem's 256: UpdateSystem inherits an anchor's offset
        /// only when the intervals match, and landing in the anchor's own sim frame is the
        /// entire point of the registration.</summary>
        public override int GetUpdateInterval(SystemUpdatePhase phase)
        {
            return WetnessInterval;
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);

            m_Armed = false;
            m_Disabled = false;
            m_ErrorLogged = false;
            m_PassCounter = 0;
            m_LastByte = -1;
            m_LastAreaCount = -1;

            if (mode != GameMode.Game)
                return;

            try
            {
                ResolveRunPrefabs();
                if (m_RunPrefabs.Count == 0)
                {
                    Mod.Log.Info("Surface: no run surface prefab present — prop snow sync " +
                                 "inert until one is imported (expected pre-import)");
                    return;
                }
                m_Armed = true;
                Mod.Log.Info("Surface: ARMED — giving run areas the Game.Objects.Surface the " +
                             "renderer looks for (SurfacePrefab only adds the empty " +
                             "Game.Areas.Surface tag, so props on runs render snow-free in " +
                             "every season) and tracking it to measured run depth; full white " +
                             $"at {Tuning.FullWhiteDepthM:F2} m, every {WetnessInterval} sim " +
                             "frames anchored after WetnessSystem");
            }
            catch (Exception e)
            {
                m_Disabled = true;
                if (!m_ErrorLogged)
                {
                    m_ErrorLogged = true;
                    Mod.Log.Error($"!!! {nameof(SkiRunSurfaceSystem)} EXCEPTION during load " +
                                  "setup — prop snow sync disabled until the next game load. " +
                                  $"Please report this stack:\n{e}");
                }
            }
        }

        protected override void OnUpdate()
        {
            try
            {
                if (m_Disabled || !m_Armed)
                    return;
                if (!m_SnowInjectionSystem.HasRunSnowMeasurement)
                    return;   // nothing measured yet — leave vanilla entirely alone

                float depth = m_SnowInjectionSystem.MeanRunSnowDepthM;
                float snow01 = math.saturate(depth / Tuning.FullWhiteDepthM);
                byte value = (byte)math.round(snow01 * 255f);

                var chunks = m_RunAreaQuery.ToArchetypeChunkArray(Allocator.Temp);
                var toWrite = new NativeList<Entity>(8, Allocator.Temp);
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
                            if (m_RunPrefabs.Contains(prefabRefs[i].m_Prefab))
                                toWrite.Add(entities[i]);
                        }
                    }
                }
                finally
                {
                    chunks.Dispose();
                }

                int added = 0;
                for (int i = 0; i < toWrite.Length; i++)
                {
                    Entity area = toWrite[i];
                    if (!EntityManager.HasComponent<Game.Objects.Surface>(area))
                    {
                        EntityManager.AddComponent<Game.Objects.Surface>(area);
                        added++;
                    }
                    var surface = EntityManager.GetComponentData<Game.Objects.Surface>(area);
                    // SET, do not clamp up: writing only upward leaves the white wash
                    // immortal, because vanilla sheds under 1/255 of accumulated snow per
                    // pass at shoulder temperatures. Tracking both directions keeps prop
                    // whiteness on the same climate model as the ribbon itself.
                    surface.m_SnowAmount = value;
                    surface.m_AccumulatedSnow = value;
                    EntityManager.SetComponentData(area, surface);
                }

                m_PassCounter++;
                bool log = value != m_LastByte || toWrite.Length != m_LastAreaCount ||
                           added > 0 || m_PassCounter % Tuning.LogEveryNth == 1;
                if (log)
                {
                    Mod.Log.Info($"Surface: {toWrite.Length} run area(s) -> snow {value}/255 " +
                                 $"(mean run depth {depth:F3} m)" +
                                 (added > 0 ? $", Game.Objects.Surface added to {added}" : ""));
                }
                m_LastByte = value;
                m_LastAreaCount = toWrite.Length;
                toWrite.Dispose();
            }
            catch (Exception e)
            {
                m_Disabled = true;
                if (!m_ErrorLogged)
                {
                    m_ErrorLogged = true;
                    Mod.Log.Error($"!!! {nameof(SkiRunSurfaceSystem)} EXCEPTION — prop snow " +
                                  "sync disabled until the next game load. Please report this " +
                                  $"stack:\n{e}");
                }
            }
        }

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
                    Mod.Log.Info($"Surface: run surface prefab '{name}' not found (tolerated)");
                    continue;
                }
                m_RunPrefabs.Add(prefabEntity);
            }
        }
    }
}
