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
//
// FLOOD WINDOW (Round F6, item D3; log-audit A2: 183 `Surface:` lines in load 2 — the value moves
// on every 256-frame pass while snow builds, ~1 s apart at 4×): the pass lines go through a
// LogWindow (LogWindow.cs) — at most one per Tuning.LogWindowS (10 s wall clock); the first after
// a quiet window is written as before, the rest are folded into one `Surface: N more pass line(s)
// in T s, folded …` line (snow min-max /255, then the last pass's full line). A pass that adds the
// Game.Objects.Surface component is forced (written at once, with any fold). A fold is flushed
// before a load (OnGamePreload) and at quit (OnDestroy; F6 fixer). Nothing else in this system
// changes. F6 fixer (review): the unchanged-value heartbeat is wall clock (Tuning.HeartbeatS
// 120 s, Ownership's CensusHeartbeatS rule) instead of every 8th pass (~8.6 s at 4×, which only
// fed the fold one unchanged "snow 255/255" summary per window).
//
// ROUND E1 (2026-10-04, stream S3; econ-round spec ch3 §8.3): the depth comes from
// SkiSnowInjectionSystem.TryGetDressDepth — the managed mean (bound injected runs, as before) or,
// in a city with no managed measurement this load (only natural runs, or only unbound injected runs),
// the natural runs' measured mean depth; with neither, vanilla is left alone as before. The pass line
// names the source. Phase, interval, the SET-not-clamp rule and FullWhiteDepthM are unchanged.

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

            /// <summary>An unchanged pass logs as a heartbeat only when no pass line was written for
            /// this many wall-clock seconds (F6 fixer; was every 8th pass = 2048 sim frames).</summary>
            public const float HeartbeatS = 120f;

            /// <summary>F6 D3 (header FLOOD WINDOW): at most one pass line per this many wall-clock
            /// seconds; the rest of a burst is folded into one summary (contract: 10 s).</summary>
            public const float LogWindowS = 10f;
        }

        // Must match WetnessSystem's interval or the anchored offset is not inherited.
        private const int WetnessInterval = 256;

        // E1 (D16): SkiRunNaturalA is left out on purpose — a natural run owns no props (no sub-objects,
        // nothing to wet or snow).
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

        // F6 D3 (header FLOOD WINDOW): folds the pass lines; reset per load, flushed before a load.
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

        /// <summary>F6 D3 (header FLOOD WINDOW): a load (or leaving to the menu) writes the folded
        /// pass lines of the city being left. Never throws: GameSystemBase.GamePreload DISABLES a
        /// system whose OnGamePreload throws (Game/GameSystemBase.cs:85-96; same in 1.6.2).</summary>
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

        /// <summary>F6 fixer (header FLOOD WINDOW): quit to desktop raises no onGamePreload, but the
        /// world's systems are destroyed while the logger still lives (LogWindow.cs header), so the
        /// folded pass lines are written here. Teardown must never throw: a failed write is dropped.</summary>
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
            m_PassLog.Reset();   // F6 D3: flushed in OnGamePreload; nothing carries over

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
                // E1 ch3 §8.3: managed run snow, else the natural runs' measured snow.
                if (!m_SnowInjectionSystem.TryGetDressDepth(out float depth, out RunDressSource source))
                    return;   // nothing measured yet — leave vanilla entirely alone

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

                float now = UnityEngine.Time.realtimeSinceStartup;
                bool log = value != m_LastByte || toWrite.Length != m_LastAreaCount ||
                           added > 0 || m_PassLog.QuietFor(now, Tuning.HeartbeatS);   // unchanged: wall-clock heartbeat
                if (log)
                {
                    // ch3 §10 L-V2: the source of the depth.
                    string body = $"{toWrite.Length} run area(s) -> snow {value}/255 " +
                                  $"(mean run depth {depth:F3} m, " +
                                  $"{(source == RunDressSource.NaturalMeasured ? "natural runs" : "managed")})" +
                                  (added > 0 ? $", Game.Objects.Surface added to {added}" : "");
                    // F6 D3 (header FLOOD WINDOW): a pass that added the component is forced.
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

        /// <summary>F6 D3 (header FLOOD WINDOW): one line for the folded passes — how many, over how
        /// long, the snow value range, then the last pass's full line.</summary>
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
