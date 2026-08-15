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
//   * Semantics: RAMPED APPROACH under the run mask (field round 5, was max-blend) —
//     r moves TOWARD target × coverage by a noisy per-texel step, never past it and
//     never down, so a thermal gate opening reads as snow guns building base patch by
//     patch instead of a light switch ("the snowmaking run pops in"). Monotone between
//     top-ups, still a structural no-op wherever natural winter snow already exceeds
//     the target (deep winter = zero raised texels, by design). Never additive, G
//     channel untouched. AMENDED field round 2 with scoped MANAGED MELT:
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
//   * Groomed runs (SkiRunGroomedA): target ~0.08 m where locally at/below
//     freezing + GroomedMarginC — an early/late-season grooming assist; in summer
//     groomed runs are bare.
//   * PER-TEXEL THERMAL LINE (field round 3): both gates evaluate at EACH TEXEL'S OWN
//     elevation inside the fold, so the snow line crosses a run — top white, bottom
//     bare, the boundary creeping with the season (the user's "melt from the bottom
//     up"). Too-warm texels route to the managed-decay mask instead.
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
using Colossal.Entities;
using Colossal.IO.AssetDatabase;
using Colossal.Mathematics;
using Colossal.Serialization.Entities;
using Game;
using Game.Areas;
using Game.Common;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
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

            // Spine rescue (field round 7, "the trails seem to almost start in the
            // middle"): the (2F+1)² blur only lets a texel reach FULL target when its
            // whole 7x7 neighborhood is in-polygon — which needs the run to be ~7
            // texels (~100 m) wide. Typical drawn runs are 30-60 m, so entire narrow
            // sections rode the sub-saturation fringe cap and read as "not started
            // yet"; only wide bowls went solid ("starts where it widens"). A second
            // 3x3 pass detects the run SPINE: texels whose immediate neighborhood is
            // (nearly) all in-polygon carry full target regardless of the wide blur.
            // The boundary ring still belongs to the coarse noise fold (dropouts +
            // fringe partials), so edges keep their ragged character — lobe depth is
            // reduced to that ring, accepted trade. Runs narrower than ~2 texels
            // (~28 m) still have no spine and stay soft: draw wider, or accept.
            public const float SpineCoverageFloor = 0.60f; // 3x3 coverage where spine ramps in
            public const float SpineSoftness = 0.30f;      // full spine at coverage 0.90

            // Managed melt (field round 2): buffer melt at run altitude is ~zero — the
            // legacy 0.35 m injections sat unchanged deep into July while the SEASONAL
            // snow line (shader-side, climate-driven) retreated past them. When a run's
            // gate is OFF (too warm to make snow), we decay snow inside OUR OWN mask by
            // this much per top-up window — guns off, groomed surface melts. Off-run
            // snow is never touched; on-run natural winter cover just melts a bit faster
            // than wilderness, which is what a dark, tracked, managed surface does.
            // Field round 3 (2026-08-12): 0.02 m/top-up drained a run in seconds of real
            // time — read as an on/off switch, not melt ("areas around the trails
            // instantly melt"). 0.004 m stretches a 0.12 m pack over ~30 top-up windows,
            // and the per-texel thermal line does the spatial shaping (bottom melts
            // first) — together it reads as weather, not a toggle.
            public const float DecayPerTopupM = 0.004f;

            // Ramp-up (field round 5): the old max-merge jumped a texel 0 -> full target
            // the tick its thermal gate opened — the run "popped in". Injection now
            // approaches the target at a noisy per-texel rate: RampPerTopupM scaled
            // RampRateMin..(Min+Span)x by the same two-octave spatial noise family as the
            // edge fold, so coarse ~112 m lobes whiten at different speeds — guns building
            // base, not a switch. The VISIBLE fade-in is time-to-white-saturation
            // (~0.02 m): ~3-20 top-up windows across a run; the slow tail past saturation
            // is invisible and only pads melt duration. Mean step ≈ DecayPerTopupM, so
            // buildup and managed melt stay symmetric.
            public const float RampPerTopupM = 0.004f;
            public const float RampRateMin = 0.25f;
            public const float RampRateSpan = 1.75f;

            // Chill bias (field round 6, "snow was made from the bottom up"): the R5 field
            // log showed a climate-time scrub dropped ambient 18.8 -> 3.0 °C in ONE top-up
            // window — the whole run gated open at once, fill order fell to pure noise, and
            // noise reads bottom-up on typical run geometry (wide solid-white base bowls,
            // narrow fringe-capped chutes). Real snowmaking physics is the fix: gun output
            // follows how far below threshold the air is (wet-bulb). Ramp rate is scaled by
            // saturate((threshold − T_local) / ChillSpanC) mapped to Floor..Floor+Gain —
            // the top of a run is ALWAYS colder at the same instant, so fill reads
            // top-down in every scenario, organic cooling and scrubs alike.
            public const float RampChillSpanC = 4f;    // °C below threshold for full output
            public const float RampChillFloor = 0.35f; // marginal-temperature gun output
            public const float RampChillGain = 1.65f;  // full-cold gun output = Floor+Gain

            // Season gates. SnowmakingMarginC MUST track the meaning (and value) of
            // SkiResortAttractivenessSystem.Tuning.SnowmakingMarginC — the attractiveness
            // score, this injection and the snow shader share one climate model, which is
            // the whole consistency argument. Groomed runs only get help while ambient
            // hovers near freezing (early-season grooming; summer = bare).
            public const float SnowmakingMarginC = 2f;

            // NIGHT RUNS ARE SNOWMAKING RUNS — identical physics BY DESIGN, not by
            // laziness (night-skiing-run.md §4.1): same guns, same water, same thermal
            // window. These are explicit ALIASES; do not "fix" the duplication by
            // diverging them — the one-climate-model consistency argument depends on it.
            // What differs is ACCOUNTING: night texels are counted separately so the
            // upkeep system can bill them at the night rate.
            public const float NightTargetDepthM = SnowmakingTargetDepthM;
            public const float NightMarginC = SnowmakingMarginC;
            // Per-texel thermal margins (field round 3): snow guns work down to
            // freezing+SnowmakingMarginC at each texel's own elevation; groomed runs get
            // held snow only where it is locally at/below freezing+GroomedMarginC.
            public const float GroomedMarginC = 0f;

            // Pack guard (field round 4): managed decay outran natural melt — runs
            // cleared FIRST out of a full winter snowpack (timelapse evidence). When the
            // map-wide mean depth says a pack covers the world, our decay stands down and
            // the runs melt WITH the landscape; decay only shapes the shoulder seasons.
            public const float PackGuardMeanDepthM = 0.02f;

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
        private const float HalfQuantum = 0.5f / 65535f; // R16_UNorm rounding tolerance
        private const string LoadKernelName = "LoadR16G16_UNorm";

        // Run surfaces — same tolerant name resolution as the other run-aware systems.
        private static readonly string[] SnowmakingSurfaceNames = { "SkiRunSnowmakingA" };
        private static readonly string[] GroomedSurfaceNames = { "SkiRunGroomedA" };
        private static readonly string[] NightSurfaceNames = { "SkiRunNightA" };
        // The gate whose ownership arms a run (DECISIONS 90 — the IsRunBound gate).
        private static readonly string[] GateNames = { "SkiResortGate" };

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
            public int NightTotal, NightActive;
            public int Triangles;
            public int MaskTexels;
            public int DecayTexels;
            // Per-type ACTIVE texel split (upkeep round): a texel claimed by two
            // overlapping runs counts once, for whichever run claimed it first —
            // no double-charging by construction.
            public int SnowmakingMaskTexels;
            public int GroomedMaskTexels;
            public int NightMaskTexels;
            // Runs skipped because no live gate owns them (DECISIONS 90) — they count
            // in the Totals (they exist) but never rasterize, so they cannot inject,
            // bill or score.
            public int UnboundRuns;
        }

        // ── Row-parallel jobs (perf round, entry 88) ─────────────────────────────────────
        // The same math the managed loops ran, split over texture rows: each worker owns
        // whole rows, so no two threads ever write the same texel (the fold READS neighbor
        // rows, but only from the read-only scratch plane — safe). Counts reduce exactly;
        // the float depth sums change only in association order, which is sub-quantum
        // against their byte-level consumers. [BurstCompile] is best-effort: if the game
        // does not Burst-compile mod jobs, these still run parallel as managed IL, which
        // alone retires the main-thread hitch this round exists to fix.

        /// <summary>Rows per work-stealing batch — coarse enough that scheduling overhead
        /// stays invisible, fine enough that a 100-row run still spreads across cores.</summary>
        private const int RowBatch = 8;

        /// <summary>Triangle pre-clipped to texel space on the main thread; jobs never
        /// touch ECS buffers.</summary>
        private struct TriData
        {
            public float2 A, B, C;      // world XZ
            public int X0, Y0, X1, Y1;  // texel bbox, clipped to the texture
        }

        private struct FoldRowPartial
        {
            public int NewMaskTexels;
            public int NewDecayTexels;
        }

        private struct MergeRowPartial
        {
            public int Injected, Lowered, RunTexels;
            public double GlobalDepthSum, RunDepthSum;
        }

        [BurstCompile]
        private struct ClearPlanesJob : IJobFor
        {
            [NativeDisableParallelForRestriction] public NativeArray<float> Mask;
            [NativeDisableParallelForRestriction] public NativeArray<float> DecayMask;
            [NativeDisableParallelForRestriction] public NativeArray<float> RampMask;

            public void Execute(int row)
            {
                int rowBase = row * TexSize;
                for (int tx = 0; tx < TexSize; tx++)
                {
                    Mask[rowBase + tx] = 0f;
                    DecayMask[rowBase + tx] = 0f;
                    RampMask[rowBase + tx] = 0f;
                }
            }
        }

        [BurstCompile]
        private struct RasterizeRowsJob : IJobFor
        {
            [ReadOnly] public NativeArray<TriData> Tris;
            [NativeDisableParallelForRestriction] public NativeArray<float> Scratch;
            public int RunY0;
            public float2 PlayOffset;
            public float2 TexelSize;

            public void Execute(int r)
            {
                const int s = Tuning.SupersampleGrid;
                const float subStep = 1f / s;
                const float subWeight = 1f / (s * s);
                int ty = RunY0 + r;
                int rowBase = ty * TexSize;
                for (int t = 0; t < Tris.Length; t++)
                {
                    TriData tri = Tris[t];
                    if (ty < tri.Y0 || ty > tri.Y1)
                        continue;
                    for (int tx = tri.X0; tx <= tri.X1; tx++)
                    {
                        int hits = 0;
                        for (int sy = 0; sy < s; sy++)
                        {
                            float wz = PlayOffset.y + (ty + (sy + 0.5f) * subStep) * TexelSize.y;
                            for (int sx = 0; sx < s; sx++)
                            {
                                float wx = PlayOffset.x + (tx + (sx + 0.5f) * subStep) * TexelSize.x;
                                if (PointInTriangle(new float2(wx, wz), tri.A, tri.B, tri.C))
                                    hits++;
                            }
                        }
                        if (hits > 0)
                            Scratch[rowBase + tx] += hits * subWeight;
                    }
                }
            }
        }

        /// <summary>The fold pass of one run — body ported VERBATIM from the managed loop
        /// (entry 88). The lapse is inlined because a job cannot call the managed
        /// SnowLineSystem, as the EXACT expression GetLocalTemperature evaluates —
        /// `ambient - (elevation - baseHeight) * scale` — with its three raw
        /// coefficients, so threshold routing is BIT-identical to the old per-texel
        /// managed call (review finding: pre-folding the coefficients shifted
        /// threshold-adjacent texels by a float ulp).</summary>
        [BurstCompile]
        private struct FoldRowsJob : IJobFor
        {
            [ReadOnly] public NativeArray<float> Scratch;
            [NativeDisableParallelForRestriction] public NativeArray<float> Mask;
            [NativeDisableParallelForRestriction] public NativeArray<float> DecayMask;
            [NativeDisableParallelForRestriction] public NativeArray<float> RampMask;
            public NativeArray<FoldRowPartial> Partials;
            [ReadOnly] public TerrainHeightData HeightData;
            public int RunY0, RunX0, RunX1;
            public float2 PlayOffset;
            public float2 TexelSize;
            public float TargetNorm;
            public float ThermalThresholdC;
            public float LapseAmbient;
            public float LapseBaseHeight;
            public float LapseScale;

            public void Execute(int r)
            {
                const int F = Tuning.EdgeFeatherTexels;
                const float windowNorm = 1f / ((2 * F + 1) * (2 * F + 1));
                int ty = RunY0 + r;
                var partial = default(FoldRowPartial);
                for (int tx = RunX0; tx <= RunX1; tx++)
                {
                    int idx = ty * TexSize + tx;
                    if (Scratch[idx] <= 0f)
                        continue; // containment: outside the polygon, emit nothing

                    float sum = 0f;
                    float sumFine = 0f; // 3x3 spine window (round 7), gathered in-loop
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
                            float coverage = math.min(Scratch[nRow + xx], 1f);
                            sum += coverage;
                            if (dy >= -1 && dy <= 1 && dx >= -1 && dx <= 1)
                                sumFine += coverage;
                        }
                    }
                    float blurred = sum * windowNorm;
                    if (blurred <= 0f)
                        continue;

                    float wx = PlayOffset.x + (tx + 0.5f) * TexelSize.x;
                    float wz = PlayOffset.y + (ty + 0.5f) * TexelSize.y;
                    float elevation = TerrainUtils.SampleHeight(ref HeightData,
                        new float3(wx, 0f, wz));
                    float localT = LapseAmbient - (elevation - LapseBaseHeight) * LapseScale;
                    if (localT > ThermalThresholdC)
                    {
                        if (DecayMask[idx] == 0f)
                        {
                            DecayMask[idx] = 1f;
                            partial.NewDecayTexels++;
                        }
                        continue;
                    }

                    float noise = 0.65f * Hash01(tx >> 3, ty >> 3, 0x5EED)
                                + 0.35f * Hash01(tx, ty, 0x51DE);
                    float t = math.saturate(
                        (blurred - (Tuning.EdgeNoiseFloor + noise * Tuning.EdgeNoiseSpan))
                        / Tuning.EdgeSoftness);
                    float tFine = math.saturate(
                        (sumFine * (1f / 9f) - Tuning.SpineCoverageFloor) / Tuning.SpineSoftness);
                    if (t <= 0f && tFine <= 0f)
                        continue;

                    float value = t >= 1f || tFine >= 1f
                        ? TargetNorm
                        : math.min(math.max(t, tFine) * (Tuning.FringeMaxDepthM / SnowHeightScaleM),
                            TargetNorm);
                    if (value > Mask[idx])
                    {
                        if (Mask[idx] == 0f)
                            partial.NewMaskTexels++;
                        Mask[idx] = value;
                    }

                    float chill = math.saturate((ThermalThresholdC - localT) / Tuning.RampChillSpanC);
                    if (chill > RampMask[idx])
                        RampMask[idx] = chill;
                }
                Partials[r] = partial;
            }
        }

        [BurstCompile]
        private struct ClearScratchRowsJob : IJobFor
        {
            [NativeDisableParallelForRestriction] public NativeArray<float> Scratch;
            public int RunY0, RunX0, RunX1;

            public void Execute(int r)
            {
                int rowBase = (RunY0 + r) * TexSize;
                for (int tx = RunX0; tx <= RunX1; tx++)
                    Scratch[rowBase + tx] = 0f;
            }
        }

        /// <summary>The CPU max-merge — body ported VERBATIM from the managed loop
        /// (entry 88); semantic comments live at the merge call site in Topup.</summary>
        [BurstCompile]
        private struct MergeRowsJob : IJobFor
        {
            [ReadOnly] public NativeArray<Ushort2> Readback;
            [ReadOnly] public NativeArray<float> Mask;
            [ReadOnly] public NativeArray<float> DecayMask;
            [ReadOnly] public NativeArray<float> RampMask;
            [NativeDisableParallelForRestriction] public NativeArray<float2> Upload;
            public NativeArray<MergeRowPartial> Partials;
            public bool PackGuard;
            public float DecayNorm;
            public float RampNormBase;

            public void Execute(int row)
            {
                const float inv = 1f / 65535f;
                int rowBase = row * TexSize;
                var p = default(MergeRowPartial);
                for (int tx = 0; tx < TexSize; tx++)
                {
                    int i = rowBase + tx;
                    Ushort2 raw = Readback[i];
                    float depth = raw.x * inv;
                    p.GlobalDepthSum += depth;
                    float target = Mask[i];
                    bool onRun = target > 0f || DecayMask[i] > 0f;
                    if (onRun)
                    {
                        p.RunDepthSum += depth;
                        p.RunTexels++;
                    }
                    // Half-quantum tolerance (round 6): sub-quantum shortfall is not a raise.
                    if (target - depth > HalfQuantum)
                    {
                        float rate = 0.65f * Hash01(tx >> 3, row >> 3, 0x6ADE)
                                   + 0.35f * Hash01(tx, row, 0x6AF7);
                        float gunOutput = Tuning.RampChillFloor +
                                          Tuning.RampChillGain * RampMask[i];
                        depth = math.min(target, depth + RampNormBase *
                            (Tuning.RampRateMin + Tuning.RampRateSpan * rate) * gunOutput);
                        p.Injected++;
                    }
                    else if (target == 0f && DecayMask[i] > 0f && depth > 0f && !PackGuard)
                    {
                        depth = math.max(0f, depth - DecayNorm);
                        p.Lowered++;
                    }
                    Upload[i] = new float2(depth, raw.y * inv);
                }
                Partials[row] = p;
            }
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
        private readonly HashSet<Entity> m_NightPrefabs = new HashSet<Entity>();
        private readonly HashSet<Entity> m_GatePrefabs = new HashSet<Entity>();
        private bool m_PrefabsResolved;            // session cache (PrefabSystem outlives loads)

        // Working buffers — created lazily on first use, kept for the session (~32 MB:
        // 4x NativeArray<float>[1M] planes + 4 MB readback + 8 MB upload staging + 8 MB GPU
        // buffer). Native rather than managed since the perf round (entry 88): every
        // per-texel loop now runs as a row-parallel Burst job, and jobs cannot touch
        // managed arrays.
        private NativeArray<float> m_Mask;         // target depth (normalized /8 m) per texel
        private NativeArray<float> m_DecayMask;    // 1 where a gate-OFF run manages melt
        private NativeArray<float> m_RampMask;     // chill factor 0..1 per texel (gun output)
        private NativeArray<float> m_Scratch;      // per-run additive coverage accumulator
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
        private float m_LastGlobalMeanDepthM;   // pack guard input (last pass's map mean)

        /// <summary>Mean snow depth (m) over the runs' masked texels, measured each merge
        /// pass — the decal system dresses trails by SNOW PRESENCE, not temperature, so
        /// green can never appear while the runs are still white (field round 4).</summary>
        public float MeanRunSnowDepthM { get; private set; }
        public bool HasRunSnowMeasurement { get; private set; }

        /// <summary>Per-type ACTIVE (thermally gated ON) masked texel counts + texel area,
        /// refreshed each top-up pass. The upkeep system charges snowmaking/grooming
        /// operating costs from EXACTLY the mask the injection writes — money, visuals
        /// and attractiveness share one climate model and cannot disagree.</summary>
        public int SnowmakingActiveTexels { get; private set; }
        public int GroomedActiveTexels { get; private set; }
        public int NightActiveTexels { get; private set; }
        public float TexelAreaM2 { get; private set; }
        public bool HasMaskMeasurement { get; private set; }
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
            if (m_Mask.IsCreated)
                m_Mask.Dispose();
            if (m_DecayMask.IsCreated)
                m_DecayMask.Dispose();
            if (m_RampMask.IsCreated)
                m_RampMask.Dispose();
            if (m_Scratch.IsCreated)
                m_Scratch.Dispose();
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
            m_LastGlobalMeanDepthM = 0f;
            MeanRunSnowDepthM = 0f;
            HasRunSnowMeasurement = false;
            SnowmakingActiveTexels = 0;
            GroomedActiveTexels = 0;
            NightActiveTexels = 0;
            TexelAreaM2 = 0f;
            HasMaskMeasurement = false;
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
                if (m_SnowmakingPrefabs.Count == 0 && m_GroomedPrefabs.Count == 0 &&
                    m_NightPrefabs.Count == 0)
                {
                    Mod.Log.Info("Snow: no run surface prefab present — injection inert until " +
                                 $"'{string.Join("', '", SnowmakingSurfaceNames)}' / " +
                                 $"'{string.Join("', '", GroomedSurfaceNames)}' is imported " +
                                 "(expected pre-import)");
                    return;
                }

                m_Armed = true;
                m_InitialTopupPending = true; // first sim tick after load tops up immediately
                Mod.Log.Info("Snow: ARMED — PER-TEXEL thermal line: snowmaking " +
                             $"{Tuning.SnowmakingTargetDepthM:F2} m where T_local(texel elev) <= " +
                             $"freezing+{Tuning.SnowmakingMarginC:F1} C, groomed " +
                             $"{Tuning.GroomedTargetDepthM:F2} m where <= " +
                             $"freezing+{Tuning.GroomedMarginC:F1} C; ramp " +
                             $"+{Tuning.RampPerTopupM:F3} m/top-up x{Tuning.RampRateMin:F2}-" +
                             $"{Tuning.RampRateMin + Tuning.RampRateSpan:F2} noisy (no pop-in); " +
                             $"too-warm texels decay " +
                             $"-{Tuning.DecayPerTopupM:F3} m/top-up (bottom-up melt); top-up every " +
                             $"{Tuning.TopupIntervalFrames} sim frames + once on first sim tick; " +
                             $"mask supersample {Tuning.SupersampleGrid}x{Tuning.SupersampleGrid}, " +
                             "fringe contained to run footprints; " +
                             $"{m_SnowmakingPrefabs.Count} snowmaking + {m_GroomedPrefabs.Count} " +
                             $"groomed + {m_NightPrefabs.Count} night run prefab(s) watched");
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
        //
        // PERF SHAPE (entry 88): every per-texel loop is a row-parallel Burst job; the
        // managed code left here is orchestration. The blocking readback is DELIBERATE and
        // stays: the merge writes the FULL texture back (the vanilla Load kernel has no
        // sub-rect parameters — verified against its complete ShaderIDs surface), so a
        // stale async snapshot would revert natural snowfall map-wide on upload. Vanilla's
        // rolling-async mirror pattern (water/terrain) is read-only and does not apply.
        // Same-frame atomicity is proven: GameSimulation systems run sequentially on the
        // main thread, SnowSystem is the sole gameplay-time writer, and same-queue GPU
        // ordering covers the rest. The per-phase ms split in the top-up log line is the
        // field instrument for whatever cost remains.
        // ─────────────────────────────────────────────────────────────────────────────────
        private void Topup()
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            double tMask = 0, tReadback = 0, tMerge = 0;

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
            tMask = timer.Elapsed.TotalMilliseconds;
            // Publish the per-type active mask before ANY early-out below: "all gates
            // off" and "no runs placed" both mean zero billable operations, and the
            // upkeep system must see that zero, not a stale count.
            float2 texelDims = playSize / TexSize;
            TexelAreaM2 = texelDims.x * texelDims.y;
            SnowmakingActiveTexels = stats.SnowmakingMaskTexels;
            GroomedActiveTexels = stats.GroomedMaskTexels;
            NightActiveTexels = stats.NightMaskTexels;
            HasMaskMeasurement = true;
            int totalRuns = stats.SnowmakingTotal + stats.GroomedTotal + stats.NightTotal;
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
                SetIdle(stats.UnboundRuns == totalRuns
                    ? $"all {totalRuns} run(s) UNBOUND — no Ski Area Welcome Sign within " +
                      "5 km owns them; place a gate and the next sweep arms them"
                    : $"all {totalRuns} run(s) inert — too warm " +
                      $"(ambient {ambient:F1} C, freezing {freezing:F1} C), nothing left " +
                      "to melt in bound footprints" +
                      (stats.UnboundRuns > 0
                          ? $", {stats.UnboundRuns} unbound (no gate in range)" : ""));
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
            tReadback = timer.Elapsed.TotalMilliseconds;

            // CPU max-merge (MergeRowsJob). R: r = max(r, target×coverage) — idempotent,
            // monotone, never reduces. G: exact ushort round-trip (k/65535 re-quantizes
            // to k), water is untouched. Deep winter: natural depth already exceeds every
            // target, injected stays 0, and the upload below is skipped — the no-op by
            // design. Ramp semantics (rounds 5/6): coarse-lobe rate noise + chill factor,
            // deterministic per texel — see the job body. Pack guard uses LAST pass's map
            // mean (one-window lag, harmless): while a winter pack covers the world,
            // decay stands down — runs melt with the landscape, never ahead of it.
            bool packGuard = m_LastGlobalMeanDepthM > Tuning.PackGuardMeanDepthM;
            int injected = 0, lowered = 0;
            double globalDepthSum = 0.0, runDepthSum = 0.0;
            int runTexels = 0;
            var mergePartials = new NativeArray<MergeRowPartial>(TexSize, Allocator.TempJob);
            try
            {
                new MergeRowsJob
                {
                    Readback = m_Readback,
                    Mask = m_Mask,
                    DecayMask = m_DecayMask,
                    RampMask = m_RampMask,
                    Upload = m_Upload,
                    Partials = mergePartials,
                    PackGuard = packGuard,
                    DecayNorm = Tuning.DecayPerTopupM / SnowHeightScaleM,
                    RampNormBase = Tuning.RampPerTopupM / SnowHeightScaleM,
                }.ScheduleParallel(TexSize, RowBatch, default).Complete();

                for (int r = 0; r < TexSize; r++)
                {
                    MergeRowPartial p = mergePartials[r];
                    injected += p.Injected;
                    lowered += p.Lowered;
                    runTexels += p.RunTexels;
                    globalDepthSum += p.GlobalDepthSum;
                    runDepthSum += p.RunDepthSum;
                }
            }
            finally
            {
                mergePartials.Dispose();
            }
            tMerge = timer.Elapsed.TotalMilliseconds;
            m_LastGlobalMeanDepthM = (float)(globalDepthSum / TexelCount) * SnowHeightScaleM;
            MeanRunSnowDepthM = runTexels > 0
                ? (float)(runDepthSum / runTexels) * SnowHeightScaleM : 0f;
            HasRunSnowMeasurement = true;

            if (injected + lowered > 0)
            {
                // The game's own Deserialize upload path (SnowSystem.cs:323-327), aimed at
                // the Read buffer only: the renderer samples it as _SnowMap immediately,
                // and the next AddSnow consumes it as _Previous — the Write buffer is
                // fully rewritten from Read every snow tick, so it needs no copy.
                m_UploadBuffer.SetData(m_Upload);
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
                             $"(-{Tuning.DecayPerTopupM:F3} m/top-up on gate-off runs) " +
                             $"({stats.SnowmakingActive}/{stats.SnowmakingTotal} snowmaking " +
                             $"@ {Tuning.SnowmakingTargetDepthM:F2} m, " +
                             $"{stats.GroomedActive}/{stats.GroomedTotal} groomed " +
                             $"@ {Tuning.GroomedTargetDepthM:F2} m, " +
                             $"{stats.NightActive}/{stats.NightTotal} night, " +
                             $"{stats.Triangles} triangle(s)), " +
                             $"ambient {ambient:F1} C, skipped: " +
                             $"{stats.SnowmakingTotal - stats.SnowmakingActive} snowmaking + " +
                             $"{stats.GroomedTotal - stats.GroomedActive} groomed run(s) fully " +
                             "below the line" +
                             (stats.UnboundRuns > 0
                                 ? $" (incl. {stats.UnboundRuns} unbound — no gate in range)"
                                 : "") +
                             $", snow line ~{m_SnowLineSystem.GetSnowLineElevation():F0} m, " +
                             $"{timer.Elapsed.TotalMilliseconds:F1} ms " +
                             $"(mask {tMask:F1} + readback {tReadback - tMask:F1} + merge " +
                             $"{tMerge - tReadback:F1} + upload " +
                             $"{timer.Elapsed.TotalMilliseconds - tMerge:F1})" +
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
        /// <summary>One scheduled run's pending results, reduced after the single
        /// Complete(): per-run Active attribution stays exactly what the sequential fold
        /// produced — a texel counts for whichever run claimed it first.</summary>
        private struct PendingRun
        {
            public int Kind; // 0 night, 1 snowmaking, 2 groomed — night-first precedence
            public NativeList<TriData> Tris;
            public NativeArray<FoldRowPartial> Partials;
        }

        private MaskStats BuildMask(float2 playOffset, float2 playSize, float ambient, float freezing)
        {
            if (!m_Mask.IsCreated)
            {
                m_Mask = new NativeArray<float>(TexelCount, Allocator.Persistent);
                m_DecayMask = new NativeArray<float>(TexelCount, Allocator.Persistent);
                m_RampMask = new NativeArray<float>(TexelCount, Allocator.Persistent);
                m_Scratch = new NativeArray<float>(TexelCount, Allocator.Persistent);
            }

            var stats = default(MaskStats);
            float2 texelSize = playSize / TexSize;
            TerrainHeightData heightData = m_TerrainSystem.GetHeightData();
            m_SnowLineSystem.GetLocalTemperatureCoefficients(out float lapseAmbient,
                out float lapseBaseHeight, out float lapseScale);

            // ONE chained job graph, ONE Complete(): clear planes, then per run
            // rasterize -> fold -> scratch re-zero. Runs are chained SEQUENTIALLY — the
            // fold max-blends into shared planes and the scratch plane is reused — which
            // is also what preserves the "first run claims the texel" attribution rule.
            // The main thread meanwhile only reads ECS buffers and builds TriData, which
            // the jobs never touch.
            JobHandle chain = new ClearPlanesJob
            {
                Mask = m_Mask,
                DecayMask = m_DecayMask,
                RampMask = m_RampMask,
            }.ScheduleParallel(TexSize, RowBatch, default);

            var pending = new List<PendingRun>();
            var chunks = m_RunScanQuery.ToArchetypeChunkArray(Allocator.Temp);
            try
            {
                var entityHandle = GetEntityTypeHandle();
                var prefabRefHandle = GetComponentTypeHandle<PrefabRef>(isReadOnly: true);
                var triangleHandle = GetBufferTypeHandle<Triangle>(isReadOnly: true);
                var nodeHandle = GetBufferTypeHandle<Node>(isReadOnly: true);
                for (int c = 0; c < chunks.Length; c++)
                {
                    var entities = chunks[c].GetNativeArray(entityHandle);
                    var prefabRefs = chunks[c].GetNativeArray(ref prefabRefHandle);
                    var triangleAccessor = chunks[c].GetBufferAccessor(ref triangleHandle);
                    var nodeAccessor = chunks[c].GetBufferAccessor(ref nodeHandle);
                    for (int i = 0; i < prefabRefs.Length; i++)
                    {
                        // Night FIRST (night-skiing-run.md §4.1): if a night prefab ever
                        // also landed in the snowmaking set, classifying night first
                        // guarantees it can never be mis-billed at the snowmaking rate.
                        bool nightRun = m_NightPrefabs.Contains(prefabRefs[i].m_Prefab);
                        bool snowmaking = !nightRun &&
                            m_SnowmakingPrefabs.Contains(prefabRefs[i].m_Prefab);
                        bool groomed = !nightRun && !snowmaking &&
                            m_GroomedPrefabs.Contains(prefabRefs[i].m_Prefab);
                        if (!nightRun && !snowmaking && !groomed)
                            continue;

                        // PER-TEXEL THERMAL LINE (2026-08-12 user round, "melt from the
                        // bottom up"): gating lives in the fold — each texel injects or
                        // decays by the temperature at ITS OWN elevation, so the snow
                        // line literally crosses a run: top white, bottom bare, boundary
                        // creeping with the season.
                        float targetM;
                        float thermalThresholdC;
                        int kind;
                        if (nightRun)
                        {
                            // Aliases of the snowmaking values — identical physics by
                            // design, separate counters for billing (§4.1).
                            stats.NightTotal++;
                            targetM = Tuning.NightTargetDepthM;
                            thermalThresholdC = freezing + Tuning.NightMarginC;
                            kind = 0;
                        }
                        else if (snowmaking)
                        {
                            stats.SnowmakingTotal++;
                            targetM = Tuning.SnowmakingTargetDepthM;
                            thermalThresholdC = freezing + Tuning.SnowmakingMarginC;
                            kind = 1;
                        }
                        else
                        {
                            stats.GroomedTotal++;
                            targetM = Tuning.GroomedTargetDepthM;
                            thermalThresholdC = freezing + Tuning.GroomedMarginC;
                            kind = 2;
                        }

                        // Tether enforcement (DECISIONS 90): unbound runs exist in the
                        // Totals but never rasterize — no snow, no bill, no score.
                        if (!IsRunBound(entities[i]))
                        {
                            stats.UnboundRuns++;
                            continue;
                        }

                        ScheduleRun(triangleAccessor[i], nodeAccessor[i],
                            playOffset, texelSize, targetM / SnowHeightScaleM,
                            thermalThresholdC, heightData, lapseAmbient,
                            lapseBaseHeight, lapseScale, kind, ref chain, pending,
                            ref stats);
                    }
                }
            }
            catch
            {
                // A managed throw mid-schedule must not leave jobs in flight against
                // arrays a later pass (or OnDestroy) will touch: drain the chain and
                // free the per-run buffers, then let the outer catch disable the system.
                // ScheduleRun reassigns `chain` after EVERY ScheduleParallel and registers
                // its buffers in `pending` BEFORE scheduling, so nothing escapes this.
                chain.Complete();
                foreach (PendingRun run in pending)
                {
                    run.Partials.Dispose();
                    run.Tris.Dispose();
                }
                // An interrupted run may have rasterized coverage whose scratch re-zero
                // never got scheduled. m_Disabled is per-LOAD but m_Scratch is per-SESSION
                // — a dirty scratch plane would read as phantom polygon coverage after the
                // next load. Restore the scratch-is-zero invariant before rethrowing.
                if (m_Scratch.IsCreated)
                {
                    new ClearScratchRowsJob
                    {
                        Scratch = m_Scratch,
                        RunY0 = 0,
                        RunX0 = 0,
                        RunX1 = TexSize - 1,
                    }.ScheduleParallel(TexSize, RowBatch, default).Complete();
                }
                throw;
            }
            finally
            {
                chunks.Dispose();
            }

            // Vanilla contract for job-side height reads (AreaResourceSystem /
            // GeometrySystem / LaneSystem precedent): register the chain as a CPU-height
            // reader so terrain writers gate on it. Redundant today — the chain completes
            // inside this same OnUpdate — but it makes the safety proof structural
            // instead of incidental.
            m_TerrainSystem.AddCPUHeightReader(chain);
            chain.Complete();

            // Reduce per-run partials in schedule order — identical attribution to the
            // old sequential fold.
            for (int p = 0; p < pending.Count; p++)
            {
                PendingRun run = pending[p];
                int newTexels = 0, newDecay = 0;
                for (int r = 0; r < run.Partials.Length; r++)
                {
                    newTexels += run.Partials[r].NewMaskTexels;
                    newDecay += run.Partials[r].NewDecayTexels;
                }
                stats.MaskTexels += newTexels;
                stats.DecayTexels += newDecay;
                if (newTexels > 0)
                {
                    if (run.Kind == 0)
                    {
                        stats.NightActive++;
                        stats.NightMaskTexels += newTexels;
                    }
                    else if (run.Kind == 1)
                    {
                        stats.SnowmakingActive++;
                        stats.SnowmakingMaskTexels += newTexels;
                    }
                    else
                    {
                        stats.GroomedActive++;
                        stats.GroomedMaskTexels += newTexels;
                    }
                }
                run.Partials.Dispose();
                run.Tris.Dispose();
            }
            return stats;
        }

        /// <summary>One run polygon: pre-clip its triangles to texel space (main thread —
        /// jobs never touch ECS buffers), then append rasterize -> fold -> scratch
        /// re-zero to the chain. Additive coverage into the scratch plane, fold into the
        /// mask over the run's texel bounds, re-zero only the touched bbox (no full-plane
        /// clears — cost stays proportional to run area). Texels beyond the playable area
        /// are clipped: the snow map ends there (doc §11.5, backdrop runs get nothing).
        /// The fold's semantic load — containment, thermal routing, edge noise, spine,
        /// fringe cap (field rounds 2-7) — lives in FoldRowsJob, ported verbatim.</summary>
        private void ScheduleRun(DynamicBuffer<Triangle> triangles, DynamicBuffer<Node> nodes,
            float2 playOffset, float2 texelSize, float targetNorm, float thermalThresholdC,
            TerrainHeightData heightData, float lapseAmbient, float lapseBaseHeight,
            float lapseScale, int kind, ref JobHandle chain, List<PendingRun> pending,
            ref MaskStats stats)
        {
            int runX0 = int.MaxValue, runY0 = int.MaxValue;
            int runX1 = int.MinValue, runY1 = int.MinValue;

            var tris = new NativeList<TriData>(math.max(triangles.Length, 1), Allocator.TempJob);
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
                tris.Add(new TriData { A = a, B = b, C = cc, X0 = x0, Y0 = y0, X1 = x1, Y1 = y1 });
            }

            if (runX0 > runX1)
            {
                tris.Dispose();
                return; // no coverage (degenerate or fully off-map)
            }

            int rowCount = runY1 - runY0 + 1;
            var partials = new NativeArray<FoldRowPartial>(rowCount, Allocator.TempJob);
            // Register for cleanup BEFORE scheduling, and reassign the caller's chain
            // (by ref) after EVERY ScheduleParallel: whatever throws mid-way, BuildMask's
            // catch-drain can always complete every scheduled job and dispose every
            // buffer (review finding — the original shape leaked both on a throw here).
            pending.Add(new PendingRun { Kind = kind, Tris = tris, Partials = partials });

            chain = new RasterizeRowsJob
            {
                Tris = tris.AsArray(),
                Scratch = m_Scratch,
                RunY0 = runY0,
                PlayOffset = playOffset,
                TexelSize = texelSize,
            }.ScheduleParallel(rowCount, RowBatch, chain);

            chain = new FoldRowsJob
            {
                Scratch = m_Scratch,
                Mask = m_Mask,
                DecayMask = m_DecayMask,
                RampMask = m_RampMask,
                Partials = partials,
                HeightData = heightData,
                RunY0 = runY0,
                RunX0 = runX0,
                RunX1 = runX1,
                PlayOffset = playOffset,
                TexelSize = texelSize,
                TargetNorm = targetNorm,
                ThermalThresholdC = thermalThresholdC,
                LapseAmbient = lapseAmbient,
                LapseBaseHeight = lapseBaseHeight,
                LapseScale = lapseScale,
            }.ScheduleParallel(rowCount, RowBatch, chain);

            chain = new ClearScratchRowsJob
            {
                Scratch = m_Scratch,
                RunY0 = runY0,
                RunX0 = runX0,
                RunX1 = runX1,
            }.ScheduleParallel(rowCount, RowBatch, chain);
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
            ResolvePrefabSet(NightSurfaceNames, m_NightPrefabs);
            ResolvePrefabSet(GateNames, m_GatePrefabs);
        }

        /// <summary>The tether's teeth (DECISIONS 90): a run only operates — injects,
        /// bills, scores — while bound to a live gate. SkiRunOwnershipSystem does the
        /// binding (nearest gate within 5 km); this is the one enforcement point, so
        /// every downstream consumer (upkeep reads active texels, attractiveness reads
        /// cover) gates for free. Unbound runs stay summer-bare; border props still
        /// spawn deliberately — installed hardware without snow is the player-visible
        /// "no gate in range" signal.</summary>
        private bool IsRunBound(Entity run)
        {
            return EntityManager.TryGetComponent(run, out Owner owner) &&
                   EntityManager.Exists(owner.m_Owner) &&
                   !EntityManager.HasComponent<Deleted>(owner.m_Owner) &&
                   EntityManager.TryGetComponent(owner.m_Owner, out PrefabRef ownerPrefab) &&
                   m_GatePrefabs.Contains(ownerPrefab.m_Prefab);
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
