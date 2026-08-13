// SkiSnowInjectionSystem — Layer 1 of the run-rendering stack (run-rendering-synthesis.md):
// writes run-shaped snow into the game's OWN snow simulation buffer, so runs render as
// native terrain snow — same triplanar shading, sparkle, altitude/aspect-correct melt,
// wind drift, save/load — with the mod contributing nothing but periodic depth top-ups.
// Architecture is docs/research/snow-injection.md; every buffer fact below was re-verified
// against the current decompile (fullsrc, Game.Simulation\SnowSystem.cs) this round:
//
//   * Storage: two ping-pong 1024x1024 R16G16_UNorm RenderTextures, enableRandomWrite,
//     bilinear/clamp (CreateTexture, :385-395). R = snow depth normalized 0..1 -> 0..8 m
//     (kSnowHeightScale = 8f, :131; SnowScaleVector = (8,1,1,1), :197). G = rain/water
//     accumulation — we preserve it bit-exactly. UV ≡ playable-area UV ⇒ ~14 m/texel
//     (TerrainSystem.playableOffset/playableArea, TerrainSystem.cs:2502-2508, :3388).
//   * Injection point: public RenderTexture SnowDepth => m_SnowHeights[Read] (:212-222) —
//     the buffer bound as _SnowMap right now AND consumed as _Previous by the next AddSnow
//     (:474), which copies it forward. A write between snow ticks is instantly visible and
//     permanently adopted by the simulation (melts, drifts, saves like natural snow).
//   * Write path: the doc's Path A (own compute kernel) needs a shipped compiled shader,
//     which this mod cannot carry — so this is the doc's PATH B using ONLY game objects:
//     blocking AsyncGPUReadback of SnowDepth (the game's own Serialize pattern, :244-266)
//     -> CPU max-merge under the run mask -> ComputeBuffer.SetData -> dispatch the vanilla
//     snowUpdate shader's LoadR16G16_UNorm kernel into SnowDepth (the game's own
//     Deserialize pattern, :323-327; shader publicly reachable via
//     AssetDatabase.global.resources.shaders.snowUpdate, :233). The blocking readback
//     closes Path B's async-latency window: read, merge, upload all happen in one frame,
//     so no natural accumulation is ever reverted.
//   * Semantics: MAX-BLEND for injection — r = max(r, target × coverage). Idempotent,
//     monotone between top-ups, and a structural no-op wherever natural winter snow
//     already exceeds the target (deep winter = zero injected texels, by design). Never
//     additive, G channel untouched. AMENDED field round 2 with scoped MANAGED MELT:
//     when a run's gate is OFF (too warm to make snow), texels inside OUR OWN mask decay
//     by Tuning.DecayPerTopupM per top-up — buffer melt at run altitude measured ~zero,
//     so without this, injected snow is immortal (legacy 0.35 m sat unchanged through
//     July while the climate-driven seasonal snow line retreated past it). Decay never
//     touches snow outside run footprints; on-run natural cover melting slightly faster
//     than wilderness is the realistic behavior of a dark, tracked, managed surface.
//
// INJECTION POLICY (the seasonal mechanic IS the theme — synthesis doc, Layer 1):
//   * Snowmaking runs (SkiRunSnowmakingA): target ~0.35 m whenever the LOCAL temperature
//     at the run's mean elevation (SnowLineSystem lapse, validated formula) is at or below
//     freezing + SnowmakingMarginC — the same margin constant meaning the attractiveness
//     score uses (snow guns do not work in warm weather; gameplay, injection and visuals
//     share one model and cannot disagree).
//   * Groomed runs (SkiRunGroomedA): target ~0.25 m, but ONLY while ambient is within
//     GroomedAmbientMarginC of freezing or below — an early/late-season grooming assist;
//     in summer groomed runs are bare.
//
// SCHEDULING & PAUSE: GameSimulation phase, anchored UpdateAfter SnowSystem (write lands
// after the frame's buffer flip — doc §2). GetUpdateInterval mirrors SnowSystem's 4 so the
// inherited offset puts every invocation right behind a snow tick; the 64-frame top-up
// cadence is self-gated off SimulationSystem.frameIndex, plus one immediate top-up on the
// first sim tick after load. Pause needs no special code: SnowSystem gates purely by phase
// (GameSimulation only runs while the sim advances and only in Game action mode —
// SimulationSystem.cs:284-287) and we mirror exactly that; no sim tick ⇒ no melt ⇒ no
// top-up needed. Editor is doubly excluded: the phase never runs there, and activation is
// hard-gated to GameMode.Game — map authors must never bake our snow into a map.
//
// MOD-REMOVAL STORY (automatic, by construction): injected snow rides inside the vanilla
// save's snow field at the vanilla fixed size. On a load without the mod it is just snow —
// it melts on the game's own schedule and is indistinguishable meanwhile. Nothing to
// clean up, no erase op needed anywhere: stop injecting and orphaned snow melts away.
//
// OBSERVABILITY (v1.2.1 discipline — silence impossible): ARMED line at load logs every
// tuning constant; one buffer-contract line per load logs format + texel scale; top-up
// lines only when the injected texel count changes or every Nth pass; idle states log
// once per reason transition; the buffer guard WARNs once and disables for the load if a
// game patch drifts the contract (failure mode is "no injected snow", never corruption).
//
// DEFENSIVE CONTRACT (ChairPinSystem precedent): never throw into the game loop —
// top-level try/catch per update and per load hook, one-shot "!!!" error log, disabled
// for the current load only, re-armed at OnGameLoadingComplete.

using System;
using System.Collections.Generic;
using Colossal.IO.AssetDatabase;
using Colossal.Mathematics;
using Colossal.Serialization.Entities;
using Game;
using Game.Areas;
using Game.Common;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace SkiResortMod.Systems
{
    public sealed partial class SkiSnowInjectionSystem : GameSystemBase
    {
        // ── Tuning — the single block, all values logged at activation ───────────────────
        private static class Tuning
        {
            // Target depths. 0.35 m reads as a solid snowmaking base at the 8 m display
            // scale (4.4% of range, ~2867 quantization steps — fringe texels keep smooth
            // gradients); groomed gets a lighter shoulder-season assist. Both far exceed
            // the estimated 5-13 mm melt sag per 64-frame window (snow-injection.md §4),
            // so the sawtooth stays sub-visual.
            // Field-calibrated 2026-08-11: 0.35/0.25 m looked identical to shallower snow
            // but took sim-MONTHS of melt to visibly recede (vanilla seasonal cover is
            // cm-scale and clears in weeks — the user watched natural snow retreat while
            // our runs sat unchanged deep into July). Targets now sit just above the
            // full-white visual threshold so summer melt clears runs on a season-like
            // timescale. Raise only with a melt-rate measurement in hand.
            public const float SnowmakingTargetDepthM = 0.12f;
            public const float GroomedTargetDepthM = 0.08f;

            // Edge raggedness (field-calibrated same day: polygon-crisp isolines + visible
            // dither pattern at the boundary). The fold blurs each run's coverage over a
            // (2F+1)² neighborhood and thresholds it against two-octave deterministic
            // noise: the contour wanders in ~30-60 m lobes instead of tracing the polygon,
            // and the partial-depth fringe breaks the snow shader's regular edge dither.
            public const int EdgeFeatherTexels = 3;      // blur radius (≈42 m at 14 m/texel)
            public const float EdgeNoiseFloor = 0.15f;   // min blurred coverage that can pass
            public const float EdgeNoiseSpan = 0.55f;    // noise-modulated threshold band
            public const float EdgeSoftness = 0.30f;     // ramp width above the threshold

            // Field round 2 (same evening): depth-proportional fringes DON'T fade visually —
            // the snow shader saturates to full white at cm-scale depth, so bilinear
            // filtering crosses saturation within ~1 m of screen space and the ragged
            // contour still reads as a cliff. Fringe texels are therefore capped at a
            // depth BELOW/AROUND saturation so partial-white rendering can exist at all;
            // only full-interior texels get the run target.
            public const float FringeMaxDepthM = 0.02f;

            // Managed melt (field round 2): buffer melt at run altitude is ~zero — the
            // legacy 0.35 m injections sat unchanged deep into July while the SEASONAL
            // snow line (shader-side, climate-driven) retreated past them. When a run's
            // gate is OFF (too warm to make snow), we decay snow inside OUR OWN mask by
            // this much per top-up window — guns off, groomed surface melts. Off-run
            // snow is never touched; on-run natural winter cover just melts a bit faster
            // than wilderness, which is what a dark, tracked, managed surface does.
            public const float DecayPerTopupM = 0.02f;

            // Season gates. SnowmakingMarginC MUST track the meaning (and value) of
            // SkiResortAttractivenessSystem.Tuning.SnowmakingMarginC — the attractiveness
            // score, this injection and the snow shader share one climate model, which is
            // the whole consistency argument. Groomed runs only get help while ambient
            // hovers near freezing (early-season grooming; summer = bare).
            public const float SnowmakingMarginC = 2f;
            public const float GroomedAmbientMarginC = 2f;

            // Cadence: melt between top-ups is millimetres against a 250-350 mm target
            // (doc §4 — even 256 frames would stay invisible), so 64 sim frames carries
            // ~4x headroom. The mask is also rebuilt every top-up: at ~14 m/texel the CPU
            // rasterization is trivial, and it makes polygon edits self-heal within one
            // window without any event-watch machinery (our 64-frame slot in GameSimulation
            // could not reliably see Created/Updated tags anyway — they are cleaned up at
            // Cleanup each frame; the splat system sees them only because it runs every
            // frame in PreCulling).
            public const int TopupIntervalFrames = 64;

            // Log every change in injected-texel count, and unconditionally every Nth
            // top-up as a heartbeat (16 top-ups = ~1024 sim frames between forced lines).
            public const int LogEveryNthTopup = 16;

            // Mask antialiasing: SupersampleGrid² point tests per texel per triangle.
            // Partial-coverage edge texels inject reduced depth — a thin-snow fringe like
            // real overspray (doc §6).
            public const int SupersampleGrid = 4;
        }

        // Structural constants (not tuning). TexSize mirrors SnowSystem's kTexSize and the
        // Load kernel's fixed 64x64x(16x16) dispatch geometry; SnowHeightScaleM mirrors
        // kSnowHeightScale (both private consts in the decompile, SnowSystem.cs:123/:131 —
        // unreadable at runtime, so a patch changing them is caught by the calibration
        // session, not the guard; a wrong scale only mis-sizes our target, max-blend
        // stays safe).
        private const int TexSize = 1024;
        private const int TexelCount = TexSize * TexSize;
        private const float SnowHeightScaleM = 8f;
        private const string LoadKernelName = "LoadR16G16_UNorm";

        // Run surfaces — same tolerant name resolution as the other run-aware systems.
        private static readonly string[] SnowmakingSurfaceNames = { "SkiRunSnowmakingA" };
        private static readonly string[] GroomedSurfaceNames = { "SkiRunGroomedA" };

        /// <summary>R16G16_UNorm texel as AsyncGPUReadback delivers it (matches the
        /// layout SnowSystem.Serialize reads back, fullsrc :244-266).</summary>
#pragma warning disable 0649 // fields are filled by the GPU readback, not by C# code
        private struct Ushort2
        {
            public ushort x; // snow depth, 0..65535 -> 0..8 m
            public ushort y; // water accumulation — preserved bit-exactly
        }
#pragma warning restore 0649

        private struct MaskStats
        {
            public int SnowmakingTotal, SnowmakingActive;
            public int GroomedTotal, GroomedActive;
            public int Triangles;
            public int MaskTexels;
            public int DecayTexels;
        }

        private SnowSystem m_SnowSystem;
        private TerrainSystem m_TerrainSystem;
        private SimulationSystem m_SimulationSystem;
        private SnowLineSystem m_SnowLineSystem;   // validated climate oracle (pull-based)
        private PrefabSystem m_PrefabSystem;

        private EntityQuery m_RunScanQuery;        // live run polygons (prefab-filtered)
        private EntityQuery m_PrefabScanQuery;     // name-resolution fallback

        private readonly HashSet<Entity> m_SnowmakingPrefabs = new HashSet<Entity>();
        private readonly HashSet<Entity> m_GroomedPrefabs = new HashSet<Entity>();
        private bool m_PrefabsResolved;            // session cache (PrefabSystem outlives loads)

        // Working buffers — created lazily on first use, kept for the session (28 MB total:
        // 2x float[1M] managed + 4 MB readback + 8 MB upload staging + 8 MB GPU buffer).
        private float[] m_Mask;                    // target depth (normalized /8 m) per texel
        private float[] m_DecayMask;               // 1 where a gate-OFF run manages melt
        private float[] m_Scratch;                 // per-run additive coverage accumulator
        private NativeArray<Ushort2> m_Readback;
        private NativeArray<float2> m_Upload;
        private ComputeBuffer m_UploadBuffer;
        private ComputeShader m_SnowUpdateShader;  // vanilla; cached after validation
        private int m_LoadKernel;
        private int m_LoadSourceId;
        private int m_ResultId;

        // Per-load state — reset and re-armed at OnGameLoadingComplete.
        private bool m_Armed;                      // GameMode.Game + run prefabs present
        private bool m_Disabled;                   // one-shot catch or buffer drift
        private bool m_ErrorLogged;                // one "!!!" with stack per load
        private bool m_BufferValidated;
        private bool m_ReadbackWarned;
        private bool m_InitialTopupPending;
        private uint m_LastTopupFrame;
        private long m_TopupCounter;
        private int m_LastInjectedTexels;
        private int m_LastLoweredTexels;
        private int m_LastMaskTexels;
        private bool m_ForceNextTopupLog;
        private string m_LastIdleReason;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_SnowSystem = World.GetOrCreateSystemManaged<SnowSystem>();
            m_TerrainSystem = World.GetOrCreateSystemManaged<TerrainSystem>();
            m_SimulationSystem = World.GetOrCreateSystemManaged<SimulationSystem>();
            m_SnowLineSystem = World.GetOrCreateSystemManaged<SnowLineSystem>();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();

            m_RunScanQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Area>(),
                    ComponentType.ReadOnly<Geometry>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                    ComponentType.ReadOnly<Triangle>(),
                    ComponentType.ReadOnly<Node>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<Temp>(),
                },
            });
            m_PrefabScanQuery = GetEntityQuery(ComponentType.ReadOnly<PrefabData>());

            m_LoadSourceId = Shader.PropertyToID("_LoadSourceR16G16_UNorm");
            m_ResultId = Shader.PropertyToID("_Result");

            Mod.Log.Info($"{nameof(SkiSnowInjectionSystem)} created");
        }

        protected override void OnDestroy()
        {
            if (m_Readback.IsCreated)
                m_Readback.Dispose();
            if (m_Upload.IsCreated)
                m_Upload.Dispose();
            m_UploadBuffer?.Release();
            m_UploadBuffer = null;
            base.OnDestroy();
        }

        /// <summary>Mirror SnowSystem's own 4-frame slot (fullsrc :226-229): the anchored
        /// UpdateAfter registration inherits the anchor's offset (the attractiveness-system
        /// precedent, verified against Game.UpdateSystem), so every invocation lands right
        /// after a snow tick. The 64-frame top-up cadence is self-gated in OnUpdate.</summary>
        public override int GetUpdateInterval(SystemUpdatePhase phase)
        {
            return 4;
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);

            // Re-arm and reset per load, whatever the mode — leaving to the menu must
            // deactivate injection too.
            m_Armed = false;
            m_Disabled = false;
            m_ErrorLogged = false;
            m_BufferValidated = false;
            m_ReadbackWarned = false;
            m_InitialTopupPending = false;
            m_TopupCounter = 0;
            m_LastInjectedTexels = -1;
            m_LastLoweredTexels = -1;
            m_LastMaskTexels = -1;
            m_ForceNextTopupLog = true;
            m_LastIdleReason = null;

            // HARD GATE: GameMode.Game only — never Editor. SnowSystem also runs in
            // EditorSimulation (SystemOrder.cs:604) but we do not register there, and this
            // gate is the second lock: a map author must never save our snow into a map.
            if (mode != GameMode.Game)
                return;

            try
            {
                ResolveRunPrefabs();
                if (m_SnowmakingPrefabs.Count == 0 && m_GroomedPrefabs.Count == 0)
                {
                    Mod.Log.Info("Snow: no run surface prefab present — injection inert until " +
                                 $"'{string.Join("', '", SnowmakingSurfaceNames)}' / " +
                                 $"'{string.Join("', '", GroomedSurfaceNames)}' is imported " +
                                 "(expected pre-import)");
                    return;
                }

                m_Armed = true;
                m_InitialTopupPending = true; // first sim tick after load tops up immediately
                Mod.Log.Info("Snow: ARMED — targets: snowmaking " +
                             $"{Tuning.SnowmakingTargetDepthM:F2} m when T_local(run mean elev) <= " +
                             $"freezing+{Tuning.SnowmakingMarginC:F1} C (attractiveness margin), " +
                             $"groomed {Tuning.GroomedTargetDepthM:F2} m when ambient <= " +
                             $"freezing+{Tuning.GroomedAmbientMarginC:F1} C; top-up every " +
                             $"{Tuning.TopupIntervalFrames} sim frames + once on first sim tick; " +
                             $"mask supersample {Tuning.SupersampleGrid}x{Tuning.SupersampleGrid}; " +
                             $"log on count change or every {Tuning.LogEveryNthTopup}th top-up; " +
                             $"{m_SnowmakingPrefabs.Count} snowmaking + {m_GroomedPrefabs.Count} " +
                             "groomed run prefab(s) watched");
            }
            catch (Exception e)
            {
                m_Disabled = true;
                if (!m_ErrorLogged)
                {
                    m_ErrorLogged = true;
                    Mod.Log.Error($"!!! {nameof(SkiSnowInjectionSystem)} EXCEPTION during load " +
                                  "setup — snow injection disabled until the next game load. " +
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

                // Self-gated cadence off the sim frame counter (uint wrap-safe). The
                // initial top-up fires on the very first invocation after load.
                uint frame = m_SimulationSystem.frameIndex;
                if (!m_InitialTopupPending &&
                    frame - m_LastTopupFrame < (uint)Tuning.TopupIntervalFrames)
                    return;
                m_InitialTopupPending = false;
                m_LastTopupFrame = frame;

                Topup();
            }
            catch (Exception e)
            {
                // Never throw into the game loop. Unmissable, with the full stack, once per
                // load; session caches are KEPT so the next load re-arms automatically.
                m_Disabled = true;
                if (!m_ErrorLogged)
                {
                    m_ErrorLogged = true;
                    Mod.Log.Error($"!!! {nameof(SkiSnowInjectionSystem)} EXCEPTION — snow " +
                                  "injection disabled until the next game load. Please report " +
                                  $"this stack:\n{e}");
                }
            }
        }

        // ─────────────────────────────────────────────────────────────────────────────────
        // The whole hot path: gate + rasterize mask, validate buffer (once), blocking
        // readback, CPU max-merge, upload via the vanilla Load kernel. Every 64 sim frames.
        // ─────────────────────────────────────────────────────────────────────────────────
        private void Topup()
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();

            float2 playOffset = m_TerrainSystem.playableOffset;
            float2 playSize = m_TerrainSystem.playableArea;
            if (!(playSize.x > 0f) || !(playSize.y > 0f))
            {
                SetIdle("terrain not ready (playableArea is zero)");
                return;
            }

            // Contract guard first, once per load — so the buffer line and any drift WARN
            // appear even on loads that stay seasonally idle throughout.
            if (!m_BufferValidated && !ValidateBuffer(playSize))
                return; // WARNed + disabled inside

            float ambient = m_SnowLineSystem.GetAmbientTemperature();
            float freezing = m_SnowLineSystem.GetFreezingTemperature();

            MaskStats stats = BuildMask(playOffset, playSize, ambient, freezing);
            int totalRuns = stats.SnowmakingTotal + stats.GroomedTotal;
            if (totalRuns == 0)
            {
                SetIdle("no run polygons placed");
                return;
            }
            // FIELD BUG FIX 2026-08-12: this early-out previously fired whenever ALL gates
            // were off — which is EXACTLY when managed decay must run. Idle only when there
            // is neither anything to inject NOR anything to decay; a gates-off pass with
            // decay coverage proceeds to the merge (0 raised, N lowered).
            if (stats.MaskTexels == 0 && stats.DecayTexels == 0)
            {
                SetIdle($"all {totalRuns} run(s) gated off — too warm " +
                        $"(ambient {ambient:F1} C, freezing {freezing:F1} C) and nothing " +
                        "left to melt in their footprints");
                return;
            }

            RenderTexture snowDepth = m_SnowSystem.SnowDepth;
            if (snowDepth == null)
            {
                SetIdle("SnowDepth is null (snow system not initialized yet)");
                return;
            }

            EnsureGpuBuffers();

            // Blocking readback — the game's own Serialize pattern (SnowSystem.cs:250-251).
            // Same-frame read/merge/upload closes the async-latency window entirely.
            AsyncGPUReadbackRequest request =
                AsyncGPUReadback.RequestIntoNativeArray(ref m_Readback, snowDepth);
            request.WaitForCompletion();
            if (request.hasError)
            {
                if (!m_ReadbackWarned)
                {
                    m_ReadbackWarned = true;
                    Mod.Log.Warn("Snow [topup]: GPU readback failed — top-up skipped, retried " +
                                 "next window (warned once per load)");
                }
                return;
            }

            // CPU max-merge. R: r = max(r, target×coverage) — idempotent, monotone, never
            // reduces. G: exact ushort round-trip (k/65535 re-quantizes to k), water is
            // untouched. Deep winter: natural depth already exceeds every target, injected
            // stays 0, and the upload below is skipped — the no-op by design.
            const float inv = 1f / 65535f;
            NativeArray<Ushort2> readback = m_Readback;
            NativeArray<float2> upload = m_Upload;
            float[] mask = m_Mask;
            float[] decayMask = m_DecayMask;
            float decayNorm = Tuning.DecayPerTopupM / SnowHeightScaleM;
            int injected = 0, lowered = 0;
            for (int i = 0; i < TexelCount; i++)
            {
                Ushort2 raw = readback[i];
                float depth = raw.x * inv;
                float target = mask[i];
                if (target > depth)
                {
                    depth = target;
                    injected++;
                }
                else if (target == 0f && decayMask[i] > 0f && depth > 0f)
                {
                    // Managed melt: guns off on this run — decay OUR footprint only.
                    depth = math.max(0f, depth - decayNorm);
                    lowered++;
                }
                upload[i] = new float2(depth, raw.y * inv);
            }

            if (injected + lowered > 0)
            {
                // The game's own Deserialize upload path (SnowSystem.cs:323-327), aimed at
                // the Read buffer only: the renderer samples it as _SnowMap immediately,
                // and the next AddSnow consumes it as _Previous — the Write buffer is
                // fully rewritten from Read every snow tick, so it needs no copy.
                m_UploadBuffer.SetData(upload);
                m_SnowUpdateShader.SetBuffer(m_LoadKernel, m_LoadSourceId, m_UploadBuffer);
                m_SnowUpdateShader.SetTexture(m_LoadKernel, m_ResultId, snowDepth);
                m_SnowUpdateShader.Dispatch(m_LoadKernel, 64, 64, 1); // 16x16 threads, mirrors vanilla
            }

            timer.Stop();
            m_TopupCounter++;
            m_LastIdleReason = null;

            bool log = m_ForceNextTopupLog ||
                       injected != m_LastInjectedTexels ||
                       lowered != m_LastLoweredTexels ||
                       stats.MaskTexels != m_LastMaskTexels ||
                       m_TopupCounter % Tuning.LogEveryNthTopup == 1;
            if (log)
            {
                m_ForceNextTopupLog = false;
                Mod.Log.Info($"Snow [topup] {injected} texel(s) raised of {stats.MaskTexels} " +
                             $"masked, {lowered} lowered by managed melt " +
                             $"(-{Tuning.DecayPerTopupM:F2} m/top-up on gate-off runs) " +
                             $"({stats.SnowmakingActive}/{stats.SnowmakingTotal} snowmaking " +
                             $"@ {Tuning.SnowmakingTargetDepthM:F2} m, " +
                             $"{stats.GroomedActive}/{stats.GroomedTotal} groomed " +
                             $"@ {Tuning.GroomedTargetDepthM:F2} m, {stats.Triangles} triangle(s)), " +
                             $"ambient {ambient:F1} C, skipped: " +
                             $"{stats.SnowmakingTotal - stats.SnowmakingActive} warm-snowmaking, " +
                             $"{stats.GroomedTotal - stats.GroomedActive} warm-groomed, " +
                             $"{timer.Elapsed.TotalMilliseconds:F1} ms" +
                             (injected == 0 && lowered == 0
                                 ? " (no-op: nothing to raise or melt)" : ""));
            }
            m_LastInjectedTexels = injected;
            m_LastLoweredTexels = lowered;
            m_LastMaskTexels = stats.MaskTexels;
        }

        /// <summary>Idle states log once per reason transition (silence impossible, spam
        /// impossible); the next successful top-up is force-logged so resumption is visible.</summary>
        private void SetIdle(string reason)
        {
            if (m_LastIdleReason == reason)
                return;
            m_LastIdleReason = reason;
            m_ForceNextTopupLog = true;
            Mod.Log.Info($"Snow [idle]: {reason} — nothing injected; re-evaluated every " +
                         $"{Tuning.TopupIntervalFrames} sim frames");
        }

        // ─────────────────────────────────────────────────────────────────────────────────
        // Buffer contract guard — the single dependency surface. Checked once per load;
        // any drift means a game patch changed snow internals: WARN once and disable.
        // Failure mode is "no injected snow", never corruption (we write nothing).
        // ─────────────────────────────────────────────────────────────────────────────────
        private bool ValidateBuffer(float2 playSize)
        {
            RenderTexture rt = m_SnowSystem.SnowDepth;
            ComputeShader shader = null;
            try
            {
                shader = AssetDatabase.global.resources.shaders.snowUpdate;
            }
            catch (Exception)
            {
                // fall through with shader == null — reported below
            }

            int2 size = m_SnowSystem.TextureSize;
            string problem = null;
            if (rt == null)
                problem = "SnowDepth is null";
            else if (size.x != TexSize || size.y != TexSize ||
                     rt.width != TexSize || rt.height != TexSize)
                problem = $"size {rt.width}x{rt.height} (TextureSize {size.x}x{size.y}) != " +
                          $"{TexSize}x{TexSize}";
            else if (rt.graphicsFormat != GraphicsFormat.R16G16_UNorm)
                problem = $"format {rt.graphicsFormat} != R16G16_UNorm";
            else if (!rt.enableRandomWrite)
                problem = "enableRandomWrite is false";
            else if (shader == null)
                problem = "snowUpdate compute shader unreachable";
            else if (!shader.HasKernel(LoadKernelName))
                problem = $"kernel '{LoadKernelName}' missing";

            if (problem != null)
            {
                m_Disabled = true;
                Mod.Log.Warn("Snow: DISABLED for this load — snow buffer drifted from the " +
                             $"decompile-verified contract ({problem}); a game update likely " +
                             "changed snow internals. Injection stays off; nothing else is " +
                             "affected. Please report this line.");
                return false;
            }

            m_SnowUpdateShader = shader;
            m_LoadKernel = shader.FindKernel(LoadKernelName);
            m_BufferValidated = true;
            Mod.Log.Info($"Snow [buffer]: '{rt.name}' {rt.width}x{rt.height} " +
                         $"{rt.graphicsFormat} ok (randomWrite {rt.enableRandomWrite}), " +
                         $"R = depth 0..1 -> 0..{SnowHeightScaleM:F0} m, G = water (preserved), " +
                         $"playable {playSize.x:F0}x{playSize.y:F0} m -> " +
                         $"{playSize.x / TexSize:F2} m/texel, kernel '{LoadKernelName}' ready");
            return true;
        }

        private void EnsureGpuBuffers()
        {
            if (!m_Readback.IsCreated)
                m_Readback = new NativeArray<Ushort2>(TexelCount, Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory);
            if (!m_Upload.IsCreated)
                m_Upload = new NativeArray<float2>(TexelCount, Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory);
            if (m_UploadBuffer == null)
                m_UploadBuffer = new ComputeBuffer(TexelCount, UnsafeUtility.SizeOf<float2>(),
                    ComputeBufferType.Default); // stride mirrors SnowSystem.Deserialize (:321)
        }

        // ─────────────────────────────────────────────────────────────────────────────────
        // Mask build: run polygons -> per-texel target depth (normalized /8 m) in
        // snow-buffer texel space. Texel (x,y) covers world
        // playableOffset + ((x,y)+0.5)/1024 * playableArea (snow UV ≡ playable UV,
        // TerrainSystem.cs:2502-2508). Per run: triangles accumulate ADDITIVE supersampled
        // coverage in a scratch plane (triangles of one polygon never overlap, so partial
        // coverages on shared texels sum correctly), then fold max(coverage × target) into
        // the mask — max-blend across runs, so overlapping runs take the deeper target.
        // ─────────────────────────────────────────────────────────────────────────────────
        private MaskStats BuildMask(float2 playOffset, float2 playSize, float ambient, float freezing)
        {
            if (m_Mask == null)
            {
                m_Mask = new float[TexelCount];
                m_DecayMask = new float[TexelCount];
                m_Scratch = new float[TexelCount]; // stays zeroed between runs by the fold
            }
            Array.Clear(m_Mask, 0, TexelCount);
            Array.Clear(m_DecayMask, 0, TexelCount);

            var stats = default(MaskStats);
            float2 texelSize = playSize / TexSize;
            bool groomedGateOpen = ambient <= freezing + Tuning.GroomedAmbientMarginC;

            var chunks = m_RunScanQuery.ToArchetypeChunkArray(Allocator.Temp);
            try
            {
                var prefabRefHandle = GetComponentTypeHandle<PrefabRef>(isReadOnly: true);
                var geometryHandle = GetComponentTypeHandle<Geometry>(isReadOnly: true);
                var triangleHandle = GetBufferTypeHandle<Triangle>(isReadOnly: true);
                var nodeHandle = GetBufferTypeHandle<Node>(isReadOnly: true);
                for (int c = 0; c < chunks.Length; c++)
                {
                    var prefabRefs = chunks[c].GetNativeArray(ref prefabRefHandle);
                    var geometries = chunks[c].GetNativeArray(ref geometryHandle);
                    var triangleAccessor = chunks[c].GetBufferAccessor(ref triangleHandle);
                    var nodeAccessor = chunks[c].GetBufferAccessor(ref nodeHandle);
                    for (int i = 0; i < prefabRefs.Length; i++)
                    {
                        bool snowmaking = m_SnowmakingPrefabs.Contains(prefabRefs[i].m_Prefab);
                        bool groomed = !snowmaking && m_GroomedPrefabs.Contains(prefabRefs[i].m_Prefab);
                        if (!snowmaking && !groomed)
                            continue;

                        // Gate-OFF runs still rasterize — into the DECAY mask, so managed
                        // melt clears them (field round 2: buffer melt alone never does).
                        float targetM;
                        bool gateOpen;
                        if (snowmaking)
                        {
                            stats.SnowmakingTotal++;
                            // Snow guns: local temperature at the run's mean elevation,
                            // via the validated lapse — the attractiveness gate verbatim.
                            Bounds3 bounds = geometries[i].m_Bounds;
                            float meanElevation = (bounds.min.y + bounds.max.y) * 0.5f;
                            gateOpen = m_SnowLineSystem.GetLocalTemperature(meanElevation) <=
                                freezing + Tuning.SnowmakingMarginC;
                            if (gateOpen)
                                stats.SnowmakingActive++;
                            targetM = Tuning.SnowmakingTargetDepthM;
                        }
                        else
                        {
                            stats.GroomedTotal++;
                            gateOpen = groomedGateOpen;
                            if (gateOpen)
                                stats.GroomedActive++;
                            targetM = Tuning.GroomedTargetDepthM;
                        }

                        RasterizeRun(triangleAccessor[i], nodeAccessor[i],
                            playOffset, texelSize, targetM / SnowHeightScaleM, gateOpen, ref stats);
                    }
                }
            }
            finally
            {
                chunks.Dispose();
            }
            return stats;
        }

        /// <summary>One run polygon: additive coverage into the scratch plane, then fold
        /// into the mask over the run's texel bounds and re-zero the scratch (no full-plane
        /// clears — cost stays proportional to run area). Texels beyond the playable area
        /// are clipped: the snow map ends there (doc §11.5, backdrop runs get nothing).</summary>
        private void RasterizeRun(DynamicBuffer<Triangle> triangles, DynamicBuffer<Node> nodes,
            float2 playOffset, float2 texelSize, float targetNorm, bool gateOpen, ref MaskStats stats)
        {
            const int s = Tuning.SupersampleGrid;
            const float subStep = 1f / s;
            const float subWeight = 1f / (s * s);

            int runX0 = int.MaxValue, runY0 = int.MaxValue;
            int runX1 = int.MinValue, runY1 = int.MinValue;

            for (int t = 0; t < triangles.Length; t++)
            {
                int3 indices = triangles[t].m_Indices;
                if (math.cmax(indices) >= nodes.Length)
                    continue; // defensive: stale triangle vs node buffer
                stats.Triangles++;
                float2 a = nodes[indices.x].m_Position.xz;
                float2 b = nodes[indices.y].m_Position.xz;
                float2 cc = nodes[indices.z].m_Position.xz;

                float2 triMin = math.min(a, math.min(b, cc));
                float2 triMax = math.max(a, math.max(b, cc));
                int x0 = math.max(0, (int)math.floor((triMin.x - playOffset.x) / texelSize.x));
                int y0 = math.max(0, (int)math.floor((triMin.y - playOffset.y) / texelSize.y));
                int x1 = math.min(TexSize - 1, (int)math.floor((triMax.x - playOffset.x) / texelSize.x));
                int y1 = math.min(TexSize - 1, (int)math.floor((triMax.y - playOffset.y) / texelSize.y));
                if (x0 > x1 || y0 > y1)
                    continue; // fully outside the playable area

                runX0 = math.min(runX0, x0);
                runY0 = math.min(runY0, y0);
                runX1 = math.max(runX1, x1);
                runY1 = math.max(runY1, y1);

                for (int ty = y0; ty <= y1; ty++)
                {
                    int rowBase = ty * TexSize;
                    for (int tx = x0; tx <= x1; tx++)
                    {
                        int hits = 0;
                        for (int sy = 0; sy < s; sy++)
                        {
                            float wz = playOffset.y + (ty + (sy + 0.5f) * subStep) * texelSize.y;
                            for (int sx = 0; sx < s; sx++)
                            {
                                float wx = playOffset.x + (tx + (sx + 0.5f) * subStep) * texelSize.x;
                                if (PointInTriangle(new float2(wx, wz), a, b, cc))
                                    hits++;
                            }
                        }
                        if (hits > 0)
                            m_Scratch[rowBase + tx] += hits * subWeight;
                    }
                }
            }

            if (runX0 > runX1)
                return; // no coverage (degenerate or fully off-map)

            // Fold pass (edge-ragged, two passes because the neighborhood blur must read
            // the scratch plane intact): per texel, blur this run's coverage over a
            // (2F+1)² window, then threshold against two-octave deterministic noise —
            // interior texels (blur ≈ 1) always pass at full target; boundary texels pass
            // partially/never depending on the local noise, so the contour wanders in
            // organic lobes over ~2-4 texels instead of tracing the polygon isoline.
            const int F = Tuning.EdgeFeatherTexels;
            const float windowNorm = 1f / ((2 * F + 1) * (2 * F + 1));
            int foldX0 = math.max(0, runX0 - F), foldX1 = math.min(TexSize - 1, runX1 + F);
            int foldY0 = math.max(0, runY0 - F), foldY1 = math.min(TexSize - 1, runY1 + F);
            for (int ty = foldY0; ty <= foldY1; ty++)
            {
                for (int tx = foldX0; tx <= foldX1; tx++)
                {
                    float sum = 0f;
                    for (int dy = -F; dy <= F; dy++)
                    {
                        int yy = ty + dy;
                        if (yy < 0 || yy >= TexSize)
                            continue;
                        int nRow = yy * TexSize;
                        for (int dx = -F; dx <= F; dx++)
                        {
                            int xx = tx + dx;
                            if (xx < 0 || xx >= TexSize)
                                continue;
                            // Edge-inclusive point tests can double-count subsamples on
                            // shared triangle edges — clamp per neighbor before summing.
                            sum += math.min(m_Scratch[nRow + xx], 1f);
                        }
                    }
                    float blurred = sum * windowNorm;
                    if (blurred <= 0f)
                        continue;

                    int idx = ty * TexSize + tx;

                    // Gate OFF: this run only marks its footprint for managed melt — a
                    // plain (noise-free) coverage test so the whole former snow footprint,
                    // fringe included, decays.
                    if (!gateOpen)
                    {
                        if (blurred > 0.05f && m_DecayMask[idx] == 0f)
                        {
                            m_DecayMask[idx] = 1f;
                            stats.DecayTexels++;
                        }
                        continue;
                    }

                    // Two octaves: coarse (8-texel cells ≈ 112 m) shapes the lobes, fine
                    // (per-texel) roughs the lobe rims. Deterministic — the contour is
                    // stable across top-ups, so no shimmer between passes.
                    float noise = 0.65f * Hash01(tx >> 3, ty >> 3, 0x5EED)
                                + 0.35f * Hash01(tx, ty, 0x51DE);
                    float t = math.saturate(
                        (blurred - (Tuning.EdgeNoiseFloor + noise * Tuning.EdgeNoiseSpan))
                        / Tuning.EdgeSoftness);
                    if (t <= 0f)
                        continue;

                    // Fringe profile (field round 2): partial texels are capped at
                    // FringeMaxDepthM — around the shader's white-saturation depth — so a
                    // visible partial-white band can exist; only full-interior texels
                    // (t == 1) carry the run target.
                    float value = t >= 1f
                        ? targetNorm
                        : math.min(t * (Tuning.FringeMaxDepthM / SnowHeightScaleM), targetNorm);
                    if (value > m_Mask[idx])
                    {
                        if (m_Mask[idx] == 0f)
                            stats.MaskTexels++;
                        m_Mask[idx] = value;
                    }
                }
            }

            // Re-zero pass: only where this run's rasterizer actually wrote.
            for (int ty = runY0; ty <= runY1; ty++)
            {
                int rowBase = ty * TexSize;
                for (int tx = runX0; tx <= runX1; tx++)
                    m_Scratch[rowBase + tx] = 0f;
            }
        }

        /// <summary>Deterministic per-cell hash noise in [0,1) — integer mix, no state,
        /// identical every top-up (a shimmering edge would read as simulation noise).</summary>
        private static float Hash01(int x, int y, int salt)
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + salt * 1442695041);
            h ^= h >> 13;
            h *= 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFFu) / 16777216f;
        }

        private static bool PointInTriangle(float2 p, float2 a, float2 b, float2 c)
        {
            // Sign test, edge-inclusive with a small tolerance (house helper, shared shape
            // with SkiRunSplatSystem).
            const float eps = 0.01f;
            float d1 = Cross(p - a, b - a);
            float d2 = Cross(p - b, c - b);
            float d3 = Cross(p - c, a - c);
            bool hasNeg = d1 < -eps || d2 < -eps || d3 < -eps;
            bool hasPos = d1 > eps || d2 > eps || d3 > eps;
            return !(hasNeg && hasPos);
        }

        private static float Cross(float2 u, float2 v)
        {
            return u.x * v.y - u.y * v.x;
        }

        // House pattern: fast PrefabID path + name-scan fallback, tolerate missing (the run
        // surfaces are NOT YET IMPORTED on some machines; the system logs its inertness once
        // per load and sleeps until they exist).
        private void ResolveRunPrefabs()
        {
            if (m_PrefabsResolved)
                return;
            m_PrefabsResolved = true;
            ResolvePrefabSet(SnowmakingSurfaceNames, m_SnowmakingPrefabs);
            ResolvePrefabSet(GroomedSurfaceNames, m_GroomedPrefabs);
        }

        private void ResolvePrefabSet(string[] names, HashSet<Entity> target)
        {
            foreach (string name in names)
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
                    Mod.Log.Info($"Snow: run surface prefab '{name}' not found (tolerated)");
                    continue;
                }
                target.Add(prefabEntity);
                Mod.Log.Info($"Snow: resolved run surface prefab '{name}'");
            }
        }
    }
}
