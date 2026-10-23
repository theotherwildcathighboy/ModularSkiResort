// Makes props standing on our ski runs whiten in step with the snow we inject: nothing gives an area entity Game.Objects.Surface, so
// props on runs render snow-free; this system adds it and keeps m_SnowAmount in step with the depth SkiSnowInjectionSystem measures.
// Phase and interval are both load-bearing: UpdateAfter<WetnessSystem> (the same tick as vanilla's wetness write) and interval 256
// (an anchor's runtime offset is inherited only when the intervals are equal).
// The value is set both ways, not clamped up: props go white as the ribbon builds and come clean as it melts.
// Game mode only; only our run prefabs; m_Wetness and m_Dirtyness stay with vanilla. With no measured depth, vanilla is left alone.
// Pass lines go through a LogWindow (Tuning.LogWindowS); a fold is flushed at OnGamePreload and OnDestroy.
// Engine notes: EngineNotes/SkiRunSurfaceSystem.md

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
            // RunSnowRules.PackedSkipMinRunDepthM (0.10 = 2 x this) depends on this value: change both.

            /// <summary>An unchanged pass logs as a heartbeat only when no pass line was written for this many wall-clock seconds.</summary>
            public const float HeartbeatS = 120f;

            /// <summary>At most one pass line per this many wall-clock seconds; the rest of a burst is folded into one summary.</summary>
            public const float LogWindowS = 10f;
        }

        // Must match WetnessSystem's interval or the anchored offset is not inherited.
        private const int WetnessInterval = 256;

        // SkiRunNaturalA is left out on purpose: a natural run owns no props (nothing to wet or snow)
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
        private int m_LastByte = -1;
        private int m_LastAreaCount = -1;

        // Folds the pass lines; reset per load, flushed before a load
        private readonly LogWindow m_PassLog = new LogWindow(Tuning.LogWindowS);

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
                    // Our runs are SurfacePrefab areas (SurfacePrefab.cs:21-23), so the 256-frame walk skips lots, districts and map tiles
                    ComponentType.ReadOnly<Game.Areas.Surface>(),
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

        /// <summary>Must equal WetnessSystem's 256: UpdateSystem inherits an anchor's offset only when the intervals match.</summary>
        public override int GetUpdateInterval(SystemUpdatePhase phase)
        {
            return WetnessInterval;
        }

        /// <summary>A load (or leaving to the menu) writes the folded pass lines of the city being left. Never throws:
        /// GameSystemBase.GamePreload disables a system whose OnGamePreload throws (Game/GameSystemBase.cs:85-96; same in 1.6.2).</summary>
        protected override void OnGamePreload(Purpose purpose, GameMode mode)
        {
            base.OnGamePreload(purpose, mode);
            try
            {
                if (m_PassLog.Count > 0)
                    WritePassSummary(UnityEngine.Time.realtimeSinceStartup);
            }
            catch (Exception e)
            {
                Mod.Log.Warn($"Surface: folded pass lines could not be written before the load " +
                             $"({e.GetType().Name}: {e.Message}) — non-fatal");
            }
        }

        /// <summary>Quit to desktop raises no onGamePreload, but the world's systems are destroyed while the logger still lives
        /// (LogWindow.cs header), so the folded pass lines are written here. Teardown must never throw: a failed write is dropped.</summary>
        protected override void OnDestroy()
        {
            try
            {
                if (m_PassLog.Count > 0)
                    WritePassSummary(UnityEngine.Time.realtimeSinceStartup);
            }
            catch (Exception)
            {
                // nothing left to report to
            }
            base.OnDestroy();
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);

            m_Armed = false;
            m_Disabled = false;
            m_ErrorLogged = false;
            m_LastByte = -1;
            m_LastAreaCount = -1;
            m_PassLog.Reset();   // flushed in OnGamePreload; nothing carries over

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
                             "frames anchored after WetnessSystem; pass lines at most one per " +
                             $"LogWindowS={Tuning.LogWindowS:0} s wall clock (a burst is folded into one summary; " +
                             $"unchanged, a heartbeat every HeartbeatS={Tuning.HeartbeatS:0} s)");
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
                // Managed run snow, else the natural runs' measured snow
                if (!m_SnowInjectionSystem.TryGetDressDepth(out float depth, out RunDressSource source))
                    return;   // nothing measured yet: leave vanilla alone

                float snow01 = math.saturate(depth / Tuning.FullWhiteDepthM);
                byte value = (byte)math.round(snow01 * 255f);

                EntityManager.CompleteDependencyBeforeRO<PrefabRef>();   // the chunk read below waits for no job
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
                    // Set, do not clamp up: upward-only leaves the white wash immortal, because vanilla sheds under 1/255 of accumulated
                    // snow per pass at shoulder temperatures
                    surface.m_SnowAmount = value;
                    surface.m_AccumulatedSnow = value;
                    EntityManager.SetComponentData(area, surface);
                }

                float now = UnityEngine.Time.realtimeSinceStartup;
                bool log = value != m_LastByte || toWrite.Length != m_LastAreaCount ||
                           added > 0 || m_PassLog.QuietFor(now, Tuning.HeartbeatS);   // unchanged: wall-clock heartbeat
                if (log)
                {
                    // The source of the depth
                    string body = $"{toWrite.Length} run area(s) -> snow {value}/255 " +
                                  $"(mean run depth {depth:F3} m, " +
                                  $"{(source == RunDressSource.NaturalMeasured ? "natural runs" : "managed")})" +
                                  (added > 0 ? $", Game.Objects.Surface added to {added}" : "");
                    // A pass that added the component is forced
                    switch (m_PassLog.Offer(now, added > 0, body, value))
                    {
                        case LogWindow.Verdict.Plain:
                            Mod.Log.Info("Surface: " + body);
                            break;
                        case LogWindow.Verdict.Summary:
                            WritePassSummary(now);
                            break;
                    }
                }
                else if (m_PassLog.TrailingDue(now))
                {
                    WritePassSummary(now);   // the end of a burst: never silent
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

        /// <summary>One line for the folded passes: how many, over how long, the snow value range, then the last pass's full line.</summary>
        private void WritePassSummary(float now)
        {
            Mod.Log.Info($"Surface: {m_PassLog.Count} more pass line(s) in {m_PassLog.SpanS(now):F0} s, folded " +
                         $"(one line per LogWindowS={Tuning.LogWindowS:0} s): snow {m_PassLog.Range(0, "F0")}/255 — last: " +
                         m_PassLog.LastText);
            m_PassLog.Wrote(now);
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
