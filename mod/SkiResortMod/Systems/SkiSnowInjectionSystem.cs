// Layer 1 of the run-rendering stack: writes run-shaped snow into the game's own snow simulation buffer (SnowSystem), so runs render
// as native terrain snow (shading, melt, wind drift, save/load); the mod contributes only periodic depth top-ups (never additive).
// Buffer: 1024x1024 R16G16_UNorm; R = depth 0..8 m, G = rain/water (untouched). Read, merge and upload happen in one frame.
// Scheduling: GameSimulation, UpdateAfter SnowSystem; GetUpdateInterval 4 like SnowSystem, 64-frame top-up cadence self-gated off
// SimulationSystem.frameIndex. Game mode only (the Editor never bakes our snow into a map); pause needs no code.
// Defensive contract (ChairPinSystem precedent): never throw into the game loop; try/catch per update and load hook, one-shot "!!!"
// log, disabled for the current load only, re-armed at OnGameLoadingComplete. The buffer guard WARNs once if a patch drifts.
// Logs: an ARMED line per load with every tuning constant. Engine notes: EngineNotes/SkiSnowInjectionSystem.md

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
using Game.Prefabs.Climate;
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
        private static class Tuning
        {
            // Target depths. 0.35 m reads as a solid snowmaking base at the 8 m display scale (4.4% of range, ~2867 quantization steps;
            // fringe texels keep smooth gradients). Both far exceed the estimated 5-13 mm melt sag per 64-frame window, so the sawtooth stays
            // sub-visual. Targets sit just above the full-white visual threshold so summer melt clears runs on a season-like timescale
            // (deeper snow took sim-months to recede while vanilla seasonal cover clears in weeks); raise only with a melt-rate measurement.
            // The snowmaking ramp's ceiling is RunSnowRules.GunStopDepthM, where the guns stop (SnowGunPlumeRules); GunTargetDepthM stays the
            // base they guarantee within a night. The groomed ceiling is the gun stop depth too.
            public const float SnowmakingTargetDepthM = RunSnowRules.GunStopDepthM;
            public const float GroomedTargetDepthM = RunSnowRules.GroomedTargetDepthM;

            // Edge raggedness: the fold blurs each run's coverage over a (2F+1)^2 neighborhood and thresholds it against two-octave
            // deterministic noise, so the contour wanders in ~30-60 m lobes instead of tracing the polygon (crisp isolines and a visible dither
            // pattern at the boundary otherwise) and the partial-depth fringe breaks the snow shader's regular edge dither.
            public const int EdgeFeatherTexels = 3;      // blur radius (≈42 m at 14 m/texel)
            public const float EdgeNoiseFloor = 0.15f;   // min blurred coverage that can pass
            public const float EdgeNoiseSpan = 0.55f;    // noise-modulated threshold band
            public const float EdgeSoftness = 0.30f;     // ramp width above the threshold

            // Fringe texels are capped at a depth below/around saturation: the snow shader saturates to full white at cm-scale depth, so
            // depth-proportional fringes do not fade (bilinear filtering crosses saturation within ~1 m and the contour still reads as a
            // cliff). Only full-interior texels get the run target.
            public const float FringeMaxDepthM = 0.02f;

            // Spine rescue: the (2F+1)^2 blur only lets a texel reach full target when its whole 7x7 neighborhood is in-polygon, i.e. runs
            // ~100 m wide; typical drawn runs are 30-60 m, so narrow sections rode the fringe cap and read as "not started yet". A second 3x3
            // pass detects the run spine: texels whose immediate neighborhood is nearly all in-polygon carry full target regardless of the wide
            // blur. The boundary ring still belongs to the coarse noise fold, so edges stay ragged. Runs narrower than ~2 texels (~28 m) have
            // no spine and stay soft.
            public const float SpineCoverageFloor = 0.60f; // 3x3 coverage where spine ramps in
            public const float SpineSoftness = 0.30f;      // full spine at coverage 0.90

            // Managed melt: when a run's gate is off (too warm to make snow), snow inside our own mask decays by this much per top-up window
            // (guns off, the groomed surface melts); off-run snow is never touched. 0.004 m stretches a 0.12 m pack over ~30 windows (a larger
            // step drained a run in seconds and read as a switch); the per-texel thermal line shapes it spatially (the bottom melts first).
            public const float DecayPerTopupM = 0.004f;

            // Ramp-up: injection approaches the target at a noisy per-texel rate (RampPerTopupM scaled RampRateMin..(Min+Span)x by the same
            // two-octave spatial noise family as the edge fold), so coarse ~112 m lobes whiten at different speeds: guns building base, not a
            // switch. The visible fade-in is time-to-white-saturation (~0.02 m), ~3-20 windows across a run. Mean step ~ DecayPerTopupM, so
            // buildup and managed melt stay symmetric.
            public const float RampPerTopupM = RunSnowRules.RampPerTopupM;   // the ramp constants live in RunSnowRules (GunWindowsToReach)
            public const float RampRateMin = RunSnowRules.RampRateMin;
            public const float RampRateSpan = RunSnowRules.RampRateSpan;

            // Chill bias: a climate-time scrub can drop ambient 18.8 -> 3.0 C in one top-up window, so the whole run gates open at once, fill
            // order falls to pure noise and noise reads bottom-up on typical run geometry. Real snowmaking physics fixes it: gun output follows
            // how far below threshold the air is (wet-bulb). Ramp rate is scaled by saturate((threshold - T_local) / ChillSpanC) mapped to
            // Floor..Floor+Gain; the top of a run is always colder at the same instant, so fill reads top-down.
            public const float RampChillSpanC = RunSnowRules.RampChillSpanC;    // °C below threshold for full output
            public const float RampChillFloor = RunSnowRules.RampChillFloor; // marginal-temperature gun output
            public const float RampChillGain = RunSnowRules.RampChillGain;  // full-cold gun output = Floor+Gain

            // Season gates. The snowmaking margin is not a constant here: it is SnowLineSystem.GetSnowmakingMarginC() (Options > Snow, clamped
            // 0..6, default 2), read once per evaluated pass and passed to BuildMask; the estimate (RunSnowRules.ThresholdC) and, through this
            // mask, the upkeep bill read the same value, so the attractiveness score, this injection and the snow shader share one climate
            // model. Groomed runs get help only while ambient hovers near freezing.
            //
            // Night runs are snowmaking runs: same guns, water and thermal window (the same margin). These are explicit aliases; do not diverge
            // them, the one-climate-model consistency depends on it. What differs is accounting: night texels are counted separately so the
            // upkeep system can bill them at the night rate.
            public const float NightTargetDepthM = SnowmakingTargetDepthM;
            // Per-texel thermal margins: snow guns work down to freezing + the snowmaking margin at each texel's own elevation; groomed runs
            // hold snow only where it is locally at/below freezing + GroomedMarginC (= RunSnowRules.GroomedMarginC, the estimate's groomed gate).
            public const float GroomedMarginC = RunSnowRules.GroomedMarginC;

            // Pack guard: managed decay outran natural melt and cleared runs first out of a full winter snowpack. When the map-wide mean depth
            // says a pack covers the world, our decay stands down and runs melt with the landscape; decay only shapes the shoulder seasons. The
            // value lives in RunSnowRules, whose PackGuard is the one guard (the merge and the packed skip both read it, so they cannot disagree).
            public const float PackGuardMeanDepthM = RunSnowRules.PackGuardMeanDepthM;

            // Cadence: melt between top-ups is millimeters against a 250-350 mm target, so 64 sim frames carries ~4x headroom. The mask is
            // rebuilt every top-up (trivial CPU rasterization at ~14 m/texel), so polygon edits self-heal within one window without event
            // watching (our 64-frame slot could not reliably see Created/Updated tags: they are cleaned up each frame). The value lives in
            // RunSnowRules (the step scale is exactly 1 at this many frames).
            public const int TopupIntervalFrames = RunSnowRules.TopupIntervalFrames;

            // Log an unchanged pass as a heartbeat only when no top-up line was written for this many wall-clock seconds.
            public const float HeartbeatS = 120f;

            // At most one top-up line per this many wall-clock seconds; the rest of a burst folds into one summary.
            public const float LogWindowS = 10f;

            // Mask antialiasing: SupersampleGrid^2 point tests per texel per triangle; partial-coverage edge texels inject reduced depth, a
            // thin-snow fringe like real overspray.
            public const int SupersampleGrid = 4;
        }

        // Structural constants (not tuning). TexSize mirrors SnowSystem's kTexSize and the Load kernel's fixed 64x64x(16x16) dispatch
        // geometry; SnowHeightScaleM mirrors kSnowHeightScale (private consts, SnowSystem.cs:123/:131, unreadable at runtime, so a patch
        // changing them is not caught by the guard; a wrong scale only mis-sizes our target).
        private const int TexSize = 1024;
        private const int TexelCount = TexSize * TexSize;
        private const float SnowHeightScaleM = 8f;
        private const float HalfQuantum = 0.5f / 65535f; // R16_UNorm rounding tolerance
        // The ramp-plane value a stopped gun run writes on its cold texels (FoldRowsJob.GunGate). Negative, so no chill (0..1) is mistaken
        // for it; the merge reads it as a run texel.
        private const float StoppedMark = -1f;
        private const string LoadKernelName = "LoadR16G16_UNorm";

        // Run surfaces — same tolerant name resolution as the other run-aware systems.
        private static readonly string[] SnowmakingSurfaceNames = { "SkiRunSnowmakingA" };
        private static readonly string[] GroomedSurfaceNames = { "SkiRunGroomedA" };
        private static readonly string[] NightSurfaceNames = { "SkiRunNightA" };
        // Natural runs are rasterized for measurement only: never injected, decayed or billed.
        private static readonly string[] NaturalSurfaceNames = { "SkiRunNaturalA" };
        // The cat track: measured only, like a natural run (the snow map under it), never injected (DECISIONS 551). Its guns' snow is the
        // made-snow model (RunSnowRules.StepTrackMadeSnow). Never add this name to an injected set.
        private static readonly string[] CatTrackMeasureNames = { "SkiRunCatTrackA" };
        // The sign test lives in SkiRunOwnershipSystem (IsBound); there is no local gate-prefab set here.

        /// <summary>R16G16_UNorm texel as AsyncGPUReadback delivers it (the layout SnowSystem.Serialize reads back, :244-266).</summary>
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
            public int ProtectedTexels;   // warm texels held by an Active protection spell (a pass with any is never idle)
            public int StoppedTexels;     // cold texels of gun runs whose guns are stopped (a pass with any is never idle)
            // Per-type active texel split: a texel claimed by two overlapping runs counts once, for whichever run claimed it first, so there
            // is no double-charging.
            public int SnowmakingMaskTexels;
            public int GroomedMaskTexels;
            public int NightMaskTexels;
            // Runs skipped because they are not bound to a live sign (SkiRunOwnershipSystem.IsBound, paid orphans included): they count in the
            // Totals but never rasterize, so they cannot inject, bill or score.
            public int UnboundRuns;
            // Natural runs: every live one, the bound ones recorded for measurement (a run with no on-map coverage is found at pre-clip and ends
            // up estimated), the unbound ones, and the ones measured this pass. Never part of the counts above.
            public int NaturalTotal, NaturalScheduled, NaturalUnbound, NaturalMeasured;
            // Of those, the cat tracks (measured only, like a natural run, since the run-types round; counted in the Natural fields too).
            public int TrackTotal, TrackScheduled;
            // Over the bound injected runs' contained texels: the cold ones (T_local <= the run's gate, counted before the edge noise), their
            // count and the wrapping sum of RunSnowRules.Mix64(texel index) (sums of the per-row fields); the dry skip's footprint test.
            public int ColdTexels, ContainedTexels;
            public ulong FootprintHash;
            // Texels measured this pass (injected and natural) and the natural chain's own time (the cost record).
            public int MeasuredTexels;
            public double NaturalMs;
        }

        /// <summary>One scheduled run's measurement for RefreshRunSnow (any of the four types). Main thread; reused list, no allocation
        /// once grown.</summary>
        private struct RunSnowInput
        {
            public Entity Run;
            public RunKind Kind;            // public RunKind (0 groomed, 1 snowmaking, 2 night, 3 natural, 4 cat track: measured only)
            public bool GunsOn;             // a cat track's gun switch (SkiRunPropSystem.CatTrackGunsOn); true for every run
            public float BottomY, TopY;     // Geometry.m_Bounds.min.y / max.y (the attraction's heights)
            public bool Measured;           // sums computed this pass from a usable snapshot
            public bool CarryOver;          // natural run with no measurement due, or no usable snapshot this pass
            public double WhiteW, FootprintW, DepthW, ExcludedW;
            // the fold's texel counts (injected runs; 0 for a natural run): contained, those cold enough for the guns, and
            // those a protection spell holds this pass. A warm texel = contained > cold.
            public int Contained, Cold, ProtectedTexels;
        }

        /// <summary>A bound natural run recorded during the chunk loop, pre-clipped and measured after the injected chain only when a
        /// measurement is due.</summary>
        private struct NaturalRun
        {
            public Entity Run;
            public float BottomY, TopY;
            public float BarNorm;           // the skiable bar (31 cm when rated black or double black)
            public RunKind Kind;            // Natural, or CatTrack (measured the same way)
            public bool GunsOn;             // a cat track's gun switch; true for a natural run
        }

        /// <summary>The snapshot a pass measures from.</summary>
        private struct SnapshotChoice
        {
            public bool Usable;
            public bool FromMeasure;
            public uint Frame;
        }

        /// <summary>One run's label change, offered to m_OpenLog after the refresh (when the city counts are known).</summary>
        private struct OpenChange
        {
            public Entity Run;
            public RunSnowState Old;
            public RunSnow Now;
        }

        // The same math the managed loops ran, split over texture rows: each worker owns whole rows, so no two threads write the same
        // texel (the fold reads neighbor rows, but only from the read-only scratch plane). Counts reduce exactly; the float depth sums
        // change only in association order, which is sub-quantum against their byte-level consumers. [BurstCompile] is best-effort: if the
        // game does not Burst-compile mod jobs, these still run parallel as managed IL.

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
            public int Protected;         // warm texels the run's protection holds this pass
            // Measurement and footprint. Never read by the routing.
            public int Contained;         // texels with Scratch > 0 (this run)
            public int Cold;              // contained texels with localT <= ThermalThresholdC (injected runs)
            public ulong FootprintHash;   // wrapping Σ RunSnowRules.Mix64((ulong)idx) over contained texels
            public float FootprintW, WhiteW, DepthW, ExcludedW;
            // The pack measure over this run's cold texels with a target (Measure only), in normalized units: sum of own target and sum of
            // min(depth, own target); and the cold texels a stopped gun run held off.
            public float GunTargetW, GunPackW;
            public int Stopped;
        }

        private struct MergeRowPartial
        {
            public int Injected, Lowered, RunTexels;
            public int Held;               // texels re-raised by warm-spell protection
            public double GlobalDepthSum, RunDepthSum;
        }

        [BurstCompile]
        private struct ClearPlanesJob : IJobFor
        {
            [NativeDisableParallelForRestriction] public NativeArray<float> Mask;
            [NativeDisableParallelForRestriction] public NativeArray<float> DecayMask;
            [NativeDisableParallelForRestriction] public NativeArray<float> RampMask;
            [NativeDisableParallelForRestriction] public NativeArray<float> ProtectMask;
            [NativeDisableParallelForRestriction] public NativeArray<ushort> Owner;

            public void Execute(int row)
            {
                int rowBase = row * TexSize;
                for (int tx = 0; tx < TexSize; tx++)
                {
                    Mask[rowBase + tx] = 0f;
                    DecayMask[rowBase + tx] = 0f;
                    RampMask[rowBase + tx] = 0f;
                    ProtectMask[rowBase + tx] = 0;
                    Owner[rowBase + tx] = 0;
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

        /// <summary>The fold pass of one run. The lapse is inlined because a job cannot call the managed SnowLineSystem, as the exact
        /// expression GetLocalTemperature evaluates, `ambient - (elevation - baseHeight) * scale`, with its three raw coefficients, so
        /// threshold routing is bit-identical to the managed call (pre-folding the coefficients shifted threshold-adjacent texels by a float
        /// ulp). The per-texel routing lives in RouteTexel. Execute adds the footprint count and hash, the cold count (dry skip) and, when a
        /// usable snapshot exists (Measure), this run's own measurement: coverage-weighted footprint, depth and skiable weight (IsSkiable
        /// with the texel's own target). Measure false: Snapshot is never read (it may hold uninitialized memory).</summary>
        [BurstCompile]
        private struct FoldRowsJob : IJobFor
        {
            [ReadOnly] public NativeArray<float> Scratch;
            [NativeDisableParallelForRestriction] public NativeArray<float> Mask;
            [NativeDisableParallelForRestriction] public NativeArray<float> DecayMask;
            [NativeDisableParallelForRestriction] public NativeArray<float> RampMask;
            public NativeArray<FoldRowPartial> Partials;
            [ReadOnly] public TerrainHeightData HeightData;
            [ReadOnly] public NativeArray<Ushort2> Snapshot;   // read only when Measure
            public bool Measure;
            public int RunY0, RunX0, RunX1;
            public float2 PlayOffset;
            public float2 TexelSize;
            public float TargetNorm;
            public float ThermalThresholdC;
            public float LapseAmbient;
            public float LapseBaseHeight;
            public float LapseScale;
            // The skiable bar (injected runs: the 15 cm bar), the ledger slot, and the warm-spell protection inputs. Protecting false = every
            // warm texel goes to the decay plane.
            public float BarNorm;
            public ushort Slot;
            [NativeDisableParallelForRestriction] public NativeArray<float> ProtectMask;
            [NativeDisableParallelForRestriction] public NativeArray<ushort> Owner;
            public bool Protecting;
            public bool Spring;
            public float HoursUsed;     // game hours of the run's current protection spell
            public float NorthSign;     // 1, or -1 for a southern latitude
            // True = this gun run's guns do not raise this pass (state Stopped or Unknown). Its cold texels then write no mask target (no
            // raise, no bill) and mark the ramp plane with StoppedMark instead, so the merge still counts them as run texels (the dress depth)
            // and the natural measurement still excludes them.
            public bool GunGate;

            public void Execute(int r)
            {
                int ty = RunY0 + r;
                var partial = default(FoldRowPartial);
                for (int tx = RunX0; tx <= RunX1; tx++)
                {
                    int idx = ty * TexSize + tx;
                    if (Scratch[idx] <= 0f)
                        continue; // containment: outside the polygon, emit nothing
                    partial.Contained++;
                    partial.FootprintHash = unchecked(partial.FootprintHash + RunSnowRules.Mix64((ulong)idx));
                    float ownValue = RouteTexel(idx, tx, ty, ref partial, out bool cold);
                    if (cold)
                        partial.Cold++;
                    if (Measure)
                    {
                        float w = math.min(Scratch[idx], 1f);
                        float dn = Snapshot[idx].x * (1f / 65535f);
                        partial.FootprintW += w;
                        partial.DepthW += w * dn;
                        if (RunSnowRules.IsSkiable(dn, ownValue, BarNorm))
                            partial.WhiteW += w;
                        // the pack measure over the texels the guns would raise (cold, with a target).
                        if (cold && ownValue > 0f)
                        {
                            partial.GunTargetW += w * ownValue;
                            partial.GunPackW += w * SnowGunPlumeRules.PackTerm(dn, ownValue);
                        }
                    }
                }
                Partials[r] = partial;
            }

            /// <summary>The fold's routing (blur, thermal test, edge noise, spine, fringe cap, chill). Returns this run's own target for the
            /// texel; cold = the thermal test passed (set right after the decay branch, before the noise test).</summary>
            private float RouteTexel(int idx, int tx, int ty, ref FoldRowPartial partial, out bool cold)
            {
                const int F = Tuning.EdgeFeatherTexels;
                const float windowNorm = 1f / ((2 * F + 1) * (2 * F + 1));
                cold = false;

                    float sum = 0f;
                    float sumFine = 0f; // 3x3 spine window, gathered in-loop
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
                        return 0f;

                    float wx = PlayOffset.x + (tx + 0.5f) * TexelSize.x;
                    float wz = PlayOffset.y + (ty + 0.5f) * TexelSize.y;
                    float elevation = TerrainUtils.SampleHeight(ref HeightData,
                        new float3(wx, 0f, wz));
                    float localT = LapseAmbient - (elevation - LapseBaseHeight) * LapseScale;
                    if (localT > ThermalThresholdC)
                    {
                        // Warm-spell protection: while this run's spell is on, a warm texel no warmer than ProtectMaxLocalC whose aspect-scaled budget is
                        // not spent is held (raised back toward the depth it had) instead of left to the managed melt. Natural runs are never folded, so
                        // never protected.
                        if (Protecting && localT <= RunSnowRules.ProtectMaxLocalC)
                        {
                            float aspect = AspectAt(wx, wz);
                            if (RunSnowRules.TexelProtected(HoursUsed, localT, Spring, aspect))
                            {
                                float held = EdgeValue(blurred, sumFine, tx, ty);
                                if (held > 0f)
                                {
                                    if (held > ProtectMask[idx])
                                    {
                                        if (ProtectMask[idx] == 0f)
                                            partial.Protected++;
                                        ProtectMask[idx] = held;
                                        Owner[idx] = Slot;
                                    }
                                    return held;
                                }
                            }
                        }
                        if (DecayMask[idx] == 0f)
                        {
                            DecayMask[idx] = 1f;
                            partial.NewDecayTexels++;
                        }
                        return 0f;
                    }
                    cold = true;

                    float value = EdgeValue(blurred, sumFine, tx, ty);
                    if (value <= 0f)
                        return 0f;
                    // guns stopped (pack at the stop depth, or not measured yet): no mask target, no raise, no bill.
                    // The stopped mark only on a texel no other run claimed (a running run's chill, >= 0, overwrites it).
                    if (GunGate)
                    {
                        if (Mask[idx] == 0f && RampMask[idx] == 0f)
                            RampMask[idx] = StoppedMark;
                        partial.Stopped++;
                        return value;
                    }
                    if (value > Mask[idx])
                    {
                        if (Mask[idx] == 0f)
                            partial.NewMaskTexels++;
                        Mask[idx] = value;
                        Owner[idx] = Slot;   // the ledger credits the run whose target the texel is raised toward
                    }

                    float chill = math.saturate((ThermalThresholdC - localT) / Tuning.RampChillSpanC);
                    if (chill > RampMask[idx])
                        RampMask[idx] = chill;
                    return value;
            }

            /// <summary>The edge shape of a texel in this run: the two-octave noise against the blurred coverage, the spine rescue and the
            /// fringe cap; shared with the protected branch. 0 = dropped by the noise, else this run's own target for the texel (the full
            /// target, or a fringe partial).</summary>
            private float EdgeValue(float blurred, float sumFine, int tx, int ty)
            {
                float noise = 0.65f * Hash01(tx >> 3, ty >> 3, 0x5EED)
                            + 0.35f * Hash01(tx, ty, 0x51DE);
                float t = math.saturate(
                    (blurred - (Tuning.EdgeNoiseFloor + noise * Tuning.EdgeNoiseSpan))
                    / Tuning.EdgeSoftness);
                float tFine = math.saturate(
                    (sumFine * (1f / 9f) - Tuning.SpineCoverageFloor) / Tuning.SpineSoftness);
                if (t <= 0f && tFine <= 0f)
                    return 0f;
                return t >= 1f || tFine >= 1f
                    ? TargetNorm
                    : math.min(math.max(t, tFine) * (Tuning.FringeMaxDepthM / SnowHeightScaleM), TargetNorm);
            }

            /// <summary>The slope aspect factor at a world position, from central differences of the heights the fold already samples (four
            /// extra SampleHeight calls, only for a warm texel under protection): the terrain normal's horizontal part is the (negated) height
            /// gradient. RunSnowRules.AspectFactor turns it into 0.5..1.5.</summary>
            private float AspectAt(float wx, float wz)
            {
                float dx = TexelSize.x;
                float dz = TexelSize.y;
                float hxp = TerrainUtils.SampleHeight(ref HeightData, new float3(wx + dx, 0f, wz));
                float hxm = TerrainUtils.SampleHeight(ref HeightData, new float3(wx - dx, 0f, wz));
                float hzp = TerrainUtils.SampleHeight(ref HeightData, new float3(wx, 0f, wz + dz));
                float hzm = TerrainUtils.SampleHeight(ref HeightData, new float3(wx, 0f, wz - dz));
                return RunSnowRules.AspectFactor((hxp - hxm) / (2f * dx), (hzp - hzm) / (2f * dz), NorthSign);
            }
        }

        /// <summary>Measure-only pass over one natural run. Writes only its own partials, never Mask, DecayMask or RampMask. Texels owned
        /// by an injected run this pass are excluded (their snow may be managed) and their weight is reported. Neighbor exclusion: the
        /// fold writes only texels inside its own raster and routes each to the decay plane (too warm), the mask (cold, passes the noise)
        /// or nowhere (cold, dropped by the noise); only mask texels are raised and only decay texels lowered, so managed snow lives exactly
        /// on `Mask > 0 || DecayMask > 0 || ProtectMask > 0` texels of this pass's footprint. The mask is this pass's and the snapshot one
        /// window older: when a neighbor is redrawn, a few edge texels can be misattributed for one window (accepted).</summary>
        [BurstCompile]
        private struct MeasureRowsJob : IJobFor
        {
            [ReadOnly] public NativeArray<float> Scratch;
            [ReadOnly] public NativeArray<float> Mask;
            [ReadOnly] public NativeArray<float> DecayMask;
            [ReadOnly] public NativeArray<float> ProtectMask;   // protected warm texels are managed snow too
            [ReadOnly] public NativeArray<float> RampMask;      // a stopped gun run's cold texels (StoppedMark) too
            [ReadOnly] public NativeArray<Ushort2> Snapshot;
            public NativeArray<FoldRowPartial> Partials;
            public int RunY0, RunX0, RunX1;
            public bool Measure;
            public float BarNorm;   // 15 cm, or 31 cm for a natural run rated black or double black

            public void Execute(int r)
            {
                int ty = RunY0 + r;
                var p = default(FoldRowPartial);
                for (int tx = RunX0; tx <= RunX1; tx++)
                {
                    int idx = ty * TexSize + tx;
                    float coverage = Scratch[idx];
                    if (coverage <= 0f)
                        continue;
                    p.Contained++;
                    float w = math.min(coverage, 1f);
                    if (Mask[idx] > 0f || DecayMask[idx] > 0f || ProtectMask[idx] > 0f || RampMask[idx] < 0f)
                    {
                        p.ExcludedW += w;
                        continue;
                    }
                    if (!Measure)
                        continue;
                    float dn = Snapshot[idx].x * (1f / 65535f);
                    p.FootprintW += w;
                    p.DepthW += w * dn;
                    if (RunSnowRules.IsSkiable(dn, 0f, BarNorm))
                        p.WhiteW += w;
                }
                Partials[r] = p;
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

        /// <summary>The CPU max-merge; semantic comments live at the merge call site in Topup.</summary>
        [BurstCompile]
        private struct MergeRowsJob : IJobFor
        {
            [ReadOnly] public NativeArray<Ushort2> Readback;
            [ReadOnly] public NativeArray<float> Mask;
            [ReadOnly] public NativeArray<float> DecayMask;
            [ReadOnly] public NativeArray<float> RampMask;
            [ReadOnly] public NativeArray<float> ProtectMask;   // the depth a protected warm texel is held toward
            [NativeDisableParallelForRestriction] public NativeArray<float> ProtectBase;   // the depth captured when protection first held it
            [NativeDisableParallelForRestriction] public NativeArray<float2> Upload;
            public NativeArray<MergeRowPartial> Partials;
            public bool PackGuard;
            public float DecayNorm;
            public float RampNormBase;
            public float ProtectRaiseNorm;   // RunSnowRules.ProtectRaisePerTopupM / 8 x the step scale

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
                    float hold = ProtectMask[i];
                    // a stopped gun run's cold texel (RampMask StoppedMark, no target) is still a run texel.
                    bool onRun = target > 0f || DecayMask[i] > 0f || hold > 0f || RampMask[i] < 0f;
                    if (onRun)
                    {
                        p.RunDepthSum += depth;
                        p.RunTexels++;
                    }
                    // Half-quantum tolerance: a sub-quantum shortfall is not a raise.
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
                    else if (hold > 0f)
                    {
                        // Protection: remember the depth this texel had when the spell began (at most the run's own target: only made snow is held, never a
                        // deeper natural pack) and raise it back toward that by one step per pass, never past it. Natural snow that falls on top simply
                        // raises the base.
                        float b = ProtectBase[i];
                        if (b <= 0f)
                            b = math.min(depth, hold);
                        else if (depth > b)
                            b = math.min(depth, hold);
                        ProtectBase[i] = b;
                        if (b - depth > HalfQuantum)
                        {
                            depth = math.min(b, depth + ProtectRaiseNorm);
                            p.Held++;
                        }
                    }
                    else if (target == 0f && DecayMask[i] > 0f && depth > 0f && !PackGuard)
                    {
                        depth = math.max(0f, depth - DecayNorm);
                        p.Lowered++;
                    }
                    if (hold <= 0f && ProtectBase[i] != 0f)
                        ProtectBase[i] = 0f;   // this pass does not hold the texel: the next spell captures it afresh
                    Upload[i] = new float2(depth, raw.y * inv);
                }
                Partials[row] = p;
            }
        }

        /// <summary>Snow-made ledger: the depth one run gained in this pass's merge, over the run's texel box, for the texels the run owns
        /// (Owner plane): x = made (the texel has a gun/groomed target and was raised toward it), y = held (warm-spell protection put
        /// melted snow back), both as sums of normalized depth, one entry per row.</summary>
        [BurstCompile]
        private struct LedgerRowsJob : IJobFor
        {
            [ReadOnly] public NativeArray<Ushort2> Readback;
            [ReadOnly] public NativeArray<float2> Upload;
            [ReadOnly] public NativeArray<float> Mask;
            [ReadOnly] public NativeArray<ushort> Owner;
            public NativeArray<float2> Partials;
            public int RunY0, RunX0, RunX1;
            public ushort Slot;

            public void Execute(int r)
            {
                int ty = RunY0 + r;
                float made = 0f, held = 0f;
                for (int tx = RunX0; tx <= RunX1; tx++)
                {
                    int idx = ty * TexSize + tx;
                    if (Owner[idx] != Slot)
                        continue;
                    float gain = Upload[idx].x - Readback[idx].x * (1f / 65535f);
                    if (gain <= 0f)
                        continue;
                    if (Mask[idx] > 0f)
                        made += gain;
                    else
                        held += gain;
                }
                Partials[r] = new float2(made, held);
            }
        }

        /// <summary>One scheduled injected run's texel box and slot, kept from the mask pass to the ledger pass.</summary>
        private struct LedgerRun
        {
            public Entity Run;
            public RunKind Kind;
            public ushort Slot;
            public int X0, Y0, X1, Y1;
        }

        private SnowSystem m_SnowSystem;
        private TerrainSystem m_TerrainSystem;
        private SimulationSystem m_SimulationSystem;
        private SnowLineSystem m_SnowLineSystem;   // validated climate oracle (pull-based)
        private PrefabSystem m_PrefabSystem;
        private SkiRunOwnershipSystem m_Ownership; // the bound test (IsRunBound)
        private SkiRunPropSystem m_RunProps;       // a cat track's gun switch (CatTrackGunsOn): Off opens on natural snow only (GetRunSnow)

        private EntityQuery m_RunScanQuery;        // live run polygons (prefab-filtered)
        private EntityQuery m_PrefabScanQuery;     // name-resolution fallback

        private readonly HashSet<Entity> m_SnowmakingPrefabs = new HashSet<Entity>();
        private readonly HashSet<Entity> m_GroomedPrefabs = new HashSet<Entity>();
        private readonly HashSet<Entity> m_NightPrefabs = new HashSet<Entity>();
        private readonly HashSet<Entity> m_NaturalPrefabs = new HashSet<Entity>();   // measured only
        private readonly HashSet<Entity> m_TrackMeasurePrefabs = new HashSet<Entity>();   // the cat track: measured only, never injected
        private bool m_PrefabsResolved;            // session cache (PrefabSystem outlives loads)

        // Working buffers: created lazily on first use, kept for the session (~36 MB: 4x NativeArray<float>[1M] planes + 4 MB readback +
        // 4 MB measure-only copy + 8 MB upload staging + 8 MB GPU buffer). Native because every per-texel loop runs as a row-parallel Burst
        // job and jobs cannot touch managed arrays. Buffer ownership: m_Readback is written only by the blocking readback of a real pass
        // and read by later passes' fold / natural measurement (when chosen) and the same pass's merge, never before m_ReadbackValid;
        // m_MeasureReadback is written only by the measure-only request and read only when chosen, never while m_MeasurePending, and a
        // request is issued only after BuildMask's chains completed.
        private NativeArray<float> m_Mask;         // target depth (normalized /8 m) per texel
        private NativeArray<float> m_DecayMask;    // 1 where a gate-OFF run manages melt
        private NativeArray<float> m_RampMask;     // chill factor 0..1 per texel (gun output)
        // ProtectMask = the depth (normalized) a protected warm texel is held toward this pass (0 elsewhere); ProtectBase =
        // the depth the merge captured when protection first held the texel (persists between passes, cleared when a pass does
        // not protect the texel and at load); Owner = the run slot (1..N, schedule order) that claimed the texel for the ledger.
        private NativeArray<float> m_ProtectMask;
        private NativeArray<float> m_ProtectBase;
        private NativeArray<ushort> m_Owner;
        private NativeArray<float> m_Scratch;      // per-run additive coverage accumulator
        private NativeArray<Ushort2> m_Readback;
        private NativeArray<Ushort2> m_MeasureReadback;   // the measure-only snapshot (4 MB)
        private NativeArray<float2> m_Upload;
        private ComputeBuffer m_UploadBuffer;
        private ComputeShader m_SnowUpdateShader;  // vanilla; cached after validation
        private int m_LoadKernel;
        private int m_LoadSourceId;
        private int m_ResultId;

        // Per-load state: reset and re-armed at OnGameLoadingComplete.
        private bool m_Armed;                      // GameMode.Game + run prefabs present
        // FoldRowsJob is the only writer of the five planes, so ClearPlanesJob runs only when the last BuildMask scheduled a run (or the
        // planes are fresh from a load / a throw).
        private bool m_PlanesDirty = true;
        // Resort-present test: the last evaluated pass found no run polygon of ours at all (the "no run polygons placed" exit); while true
        // and ResortPresence.Present is false OnUpdate returns before any query or sync.
        private bool m_NoRunsSettled;
        private bool m_Disabled;                   // one-shot catch or buffer drift
        private bool m_ErrorLogged;                // one "!!!" with stack per load
        private bool m_BufferValidated;
        private bool m_ReadbackWarned;
        private bool m_InitialTopupPending;
        private uint m_LastTopupFrame;
        private int m_LastInjectedTexels;
        private int m_LastLoweredTexels;
        private int m_LastMaskTexels;
        // The previous pass's gate counts (snowmaking, groomed, night: active, total; unbound runs; mask empty (1) or not (0)) and when a
        // gate change last forced a line (at most one forced line per LogWindowS).
        private int4 m_LastGateA;
        private int4 m_LastGateB;
        private float m_LastGateForceAt;
        private float m_LastGlobalMeanDepthM;   // pack guard input (last pass's map mean)

        private ClimateSystem m_ClimateSystem;       // the current season's name (spring halves the budget)
        private PlanetarySystem m_PlanetarySystem;   // the latitude's sign (which way "north" faces)
        private SkiRunCatalogSystem m_Catalog;       // the run's rating (natural black needs 31 cm); resolved lazily, may stay null
        private readonly Dictionary<Entity, RunSnowRules.ProtectRun> m_Protect = new Dictionary<Entity, RunSnowRules.ProtectRun>();
        private readonly Dictionary<Entity, RunSnowRules.RunLedger> m_Ledger = new Dictionary<Entity, RunSnowRules.RunLedger>();
        private readonly List<LedgerRun> m_LedgerRuns = new List<LedgerRun>();
        private bool m_PassSpring;                   // this pass: the budget halves
        private bool m_PassGunSeason = true;         // this pass is in the gun season (SnowGunPlumeRules.InGunSeason)
        // The gun season is known only from the first evaluated pass of a load (the same idea as OffSeasonRules.AwaitsFirstObservation for
        // lodge staff): until then the hub's gun line reads Waiting, no estimate credits the guns, and a cat track holds its saved made snow.
        private bool m_GunSeasonKnown;
        // Each bound cat track's made snow (RunSnowRules.TrackMadeSnow), seeded from its saved SkiTrackSnow on its first refresh of a load;
        // swapped each refresh like the table. m_TrackSaves: the SkiTrackSnow values to write after the refresh.
        private Dictionary<Entity, RunSnowRules.TrackMadeSnow> m_TrackMade = new Dictionary<Entity, RunSnowRules.TrackMadeSnow>();
        private Dictionary<Entity, RunSnowRules.TrackMadeSnow> m_TrackMadeNext = new Dictionary<Entity, RunSnowRules.TrackMadeSnow>();
        private readonly List<KeyValuePair<Entity, int>> m_TrackSaves = new List<KeyValuePair<Entity, int>>();
        private int m_GunSeasonLogged = -1;          // the last season state logged (-1 none this load)
        private float m_GunWinterStart = SnowLineRules.CalendarWinterStart;   // this climate's winter, read once per load
        private float m_GunWinterEnd = SnowLineRules.CalendarWinterEnd;
        private float m_PassNorthSign = 1f;          // this pass: -1 for a southern latitude
        private int m_ProtectActiveRuns;             // runs in an Active spell at the last refresh: a real pass is due (no skip)
        private uint m_ProtectLastFrame;             // the frame of the last refresh that stepped the machines
        private bool m_ProtectFrameValid;
        private double m_MadeLoggedTotalM3;          // the Snow [made] line is written only when the total moved

        // Snapshot validity per load: a buffer is never read before a copy into it succeeded in this load (no garbage or previous-city snow).
        private bool m_ReadbackValid;
        private uint m_ReadbackFrame;
        private bool m_MeasureValid;
        private uint m_MeasureFrame;
        // Measure-only readback: one request in flight (vanilla BaseDataReader pattern).
        private AsyncGPUReadbackRequest m_MeasureRequest;
        private bool m_MeasurePending;
        private int m_LoadSerial;                // ++ per load; a request completing in a later load is discarded
        private int m_MeasureRequestLoad;
        private uint m_MeasureRequestFrame;
        private int m_MeasureRequestRenderFrame;
        private double m_MeasureRequestMs;
        private int m_MeasureFailures;           // in a row
        private bool m_MeasureStopped;           // MeasureFailLimit reached: off for this load
        private bool m_MeasureWarned;            // once per load
        private bool m_MeasureFirstLogged;       // once per load
        private bool m_InMeasureOnly;            // a measure-only episode is open
        private int m_MeasureEpisodeReadbacks;
        private int m_MeasureEpisodeLatMin, m_MeasureEpisodeLatMax;
        // Dry skip, and the packed skip (the record and the episode are shared).
        private DryPass m_LastReal;
        private uint m_LastRealPassFrame;
        private float m_LastPassRealTime = float.NegativeInfinity;   // the 0.5 s wall-clock floor
        private bool m_InSkip;
        private int m_SkipEpisodeCount;
        private float m_SkipEpisodeStartAt;
        private int m_SkipsSinceLastReal;        // for the top-up line's suffix
        // The packed skip beside the dry one, and the Snow [day] count. All reset in OnGameLoadingComplete; m_UncalmRuns also in ClearRunSnow.
        private TopupSkip m_SkipKind;            // the open episode's kind (None when no episode)
        private TopupSkip m_LastSkipKind;        // the kind of the skips since the last real pass (the top-up line's suffix)
        private int m_UncalmRuns;                // measured runs whose share is in [30 %, 70 %) at the last refresh
        private TimeSystem m_TimeSystem;         // Snow [day] (set in OnCreate, the SkiGuestCounterSystem idiom)
        private int m_DayKey = int.MinValue;     // SkiDayRules.DayKey of the day being counted
        private bool m_DayFromLoad;              // the day being counted began at this load
        private string m_DayText;                // "YYYY-MM" (+ " (day key N)" when a year is not 12 days)
        private int m_DayPasses, m_DayReal, m_DayForced, m_DayDry, m_DayPacked;
        private double m_DayRealMs, m_DaySkipMs;
        // Natural measurement due: the inputs of the last natural chain.
        private uint m_LastNaturalSnapshotFrame;
        private bool m_LastNaturalFromMeasure;
        private ulong m_LastNaturalSignature;
        private ulong m_LastNaturalInjectedHash;
        private readonly List<NaturalRun> m_NaturalRuns = new List<NaturalRun>();
        private readonly List<PendingRun> m_NaturalPending = new List<PendingRun>();
        // The per-run table: swapped like m_RunTexels (no allocation once grown); main thread.
        private Dictionary<Entity, RunSnow> m_RunSnow = new Dictionary<Entity, RunSnow>();
        private Dictionary<Entity, RunSnow> m_RunSnowNext = new Dictionary<Entity, RunSnow>();
        private readonly List<RunSnowInput> m_RunSnowInputs = new List<RunSnowInput>();
        private readonly List<OpenChange> m_OpenChanges = new List<OpenChange>();
        private int m_RunSnowGeneration;
        private SnowSummary m_Summary;
        private int m_MultFull, m_MultPartly, m_MultZero;   // by multiplier (the hub's marks), for the top-up line
        private bool m_NaturalDressValid;
        private float m_NaturalDressDepthM;
        private bool m_FirstMeasuredLogged;      // once per load
        private bool m_LastSnapUsable;           // a usable snapshot was seen this load
        private bool m_StaleLogged;              // written for the current stale spell
        private uint m_LastUsableSnapFrame;
        private bool m_SourceDisabledLogged;     // once per load
        // SnowSettled: the clock starts at the first evaluated pass of the load (also one
        // that idles before RefreshRunSnow, second pass); m_TableAllMeasured is set by each refresh.
        private bool m_FirstPassSet;
        private uint m_FirstPassFrame;
        private uint m_LastEvalFrame;
        private bool m_TableAllMeasured;
        private const uint SnowSettledFrames = 256;
        // Settings seen at the last evaluated pass.
        private float m_LastMarginC;
        private bool m_LastGating;
        // Cost record: the first CostPasses evaluated passes of the load.
        private const int CostPasses = 64;
        private const int CostMinPassesAtPreload = 8;
        private bool m_CostLogged;
        private bool m_CostThisPass;             // the current evaluated pass is inside the record
        private int m_CostPasses;
        private float m_CostFirstAt, m_CostLastAt;
        private int m_CostReal;
        private double m_CostRealMask, m_CostRealReadback, m_CostRealMerge, m_CostRealUpload;
        private int m_CostSkipped;
        private int m_CostSkippedPacked;         // the packed part of m_CostSkipped
        private double m_CostSkippedMask;
        private int m_CostNaturalPasses, m_CostNaturalRunsMax;
        private double m_CostNaturalMs;
        private int m_CostMeasureRequests;
        private double m_CostMeasureRequestMs;
        private int m_CostLatencyMin, m_CostLatencyMax;
        private long m_CostMeasuredTexels;
        private int m_StepClampedPasses;         // evaluated passes where the x4 cap binds

        /// <summary>Mean snow depth (m) over the runs' masked texels, measured each merge pass. The decal system dresses trails by snow
        /// presence, not temperature, so green can never appear while the runs are still white.</summary>
        public float MeanRunSnowDepthM { get; private set; }
        public bool HasRunSnowMeasurement { get; private set; }

        /// <summary>Per-type active (thermally gated on) masked texel counts + texel area, refreshed each top-up pass. The upkeep system
        /// charges snowmaking/grooming operating costs from exactly the mask the injection writes, so money, visuals and attractiveness
        /// share one climate model.</summary>
        public int SnowmakingActiveTexels { get; private set; }
        public int GroomedActiveTexels { get; private set; }
        public int NightActiveTexels { get; private set; }
        public float TexelAreaM2 { get; private set; }
        public bool HasMaskMeasurement { get; private set; }

        // Each mask pass (BuildMask, every top-up) refills a run -> active-texel map from the per-run reduction that already exists: a
        // run's count is its NewMaskTexels, the texels it claimed first. Attribution is first-claim in schedule order (FoldRowsJob counts a
        // texel only when Mask[idx] was still 0, and runs are chained sequentially), so the per-run counts sum exactly to the billed
        // Snowmaking/Groomed/NightActiveTexels, but a run overlapped by an earlier one, or narrower than the ~14 m texel grid, can read 0
        // while making snow: > 0 means "making snow", 0 does not mean "off". Only bound runs with on-map coverage are scheduled, so an
        // unbound or off-map run has no entry (false). RunTexelGeneration bumps only when the map's content changed (a run added / removed
        // or any count changed). Two dictionaries are swapped, so a pass allocates nothing once both have grown. Main thread (filled after
        // chain.Complete()).
        private Dictionary<Entity, int> m_RunTexels = new Dictionary<Entity, int>();
        private Dictionary<Entity, int> m_RunTexelsNext = new Dictionary<Entity, int>();
        private int m_RunTexelGeneration;

        /// <summary>++ only when the per-run active-texel map's content changed.</summary>
        internal int RunTexelGeneration => m_RunTexelGeneration;

        // m_GunState: the hysteresis state per bound, scheduled snowmaking / night run, stepped after every mask pass from the pack that
        // pass measured (SnowGunPlumeRules.NextGunState) and used by the next pass's fold (FoldRowsJob.GunGate). m_GunRunning: "the guns of
        // this run ran this pass" (the state the fold used and a cold texel), the read for the plume system. Two dictionaries each, swapped
        // (runs that left the schedule drop out); cleared at a load and when the injection disables. Main thread.
        private Dictionary<Entity, SnowGunPlumeRules.GunState> m_GunState = new Dictionary<Entity, SnowGunPlumeRules.GunState>();
        private Dictionary<Entity, SnowGunPlumeRules.GunState> m_GunStateNext = new Dictionary<Entity, SnowGunPlumeRules.GunState>();
        private Dictionary<Entity, bool> m_GunRunning = new Dictionary<Entity, bool>();
        private Dictionary<Entity, bool> m_GunRunningNext = new Dictionary<Entity, bool>();
        private readonly Dictionary<Entity, float> m_GunPackM = new Dictionary<Entity, float>();   // last measured pack (logs, dump)
        private int m_GunGeneration;

        /// <summary>++ whenever any run's guns-running flag changes or a run enters or leaves the map.</summary>
        internal int GunGeneration => m_GunGeneration;

        /// <summary>Whether the guns of <paramref name="run"/> ran in the last mask pass (SnowGunPlumeRules.GunsRunning).
        /// False when the run was not scheduled (unbound, off the map, not a gun run, injection idle or disabled).</summary>
        /// <summary>Whether the last pass was in the gun season (the run tooltip's "out of season"). Before the first pass of a load
        /// (GunSeasonKnown false) this is true, so HubRules.GunWhyOf, which callers then pass stateKnown false, answers Waiting and never a
        /// season it has not seen (the last load's value would be stale).</summary>
        internal bool GunSeasonNow => !m_GunSeasonKnown || m_PassGunSeason;

        /// <summary>False from a load to its first evaluated pass: GunSeasonNow is not observed yet (the hub's gun line waits).</summary>
        internal bool GunSeasonKnown => m_GunSeasonKnown;

        /// <summary>Whether a cat track's guns can run: bound (only bound runs are scheduled or billed, as for a gun run), the gun season and part of
        /// the track cold enough at the day-mean temperature (SnowGunPlumeRules.TrackGunsCanRun; asked with the guns on, so in the season the
        /// share is the guns' own reach, freezing + the margin). No state: a cat track stays out of the fold.</summary>
        internal bool CatTrackGunsCanRun(Entity run, float bottomY, float topY)
        {
            return IsRunBound(run) && SnowGunPlumeRules.TrackGunsCanRun(m_GunSeasonKnown && m_PassGunSeason, EstimateShare(RunKind.CatTrack, bottomY, topY));
        }

        internal bool TryGetRunGunsRunning(Entity run, out bool running)
        {
            return m_GunRunning.TryGetValue(run, out running);
        }

        /// <summary>The run's hysteresis state and last measured pack (m; NaN before the first measurement).</summary>
        internal bool TryGetRunGunState(Entity run, out SnowGunPlumeRules.GunState state, out float packM)
        {
            if (!m_GunPackM.TryGetValue(run, out packM))
                packM = float.NaN;
            return m_GunState.TryGetValue(run, out state);
        }

        /// <summary>One gun run after the reduction: record the flag the fold used this pass, step the state from the pack this pass
        /// measured (NaN: kept), and write one line per start / stop (the hysteresis bounds how often).</summary>
        private void StepGuns(PendingRun run, int cold, float packM)
        {
            SnowGunPlumeRules.GunState before = run.GunState;
            SnowGunPlumeRules.GunState next = SnowGunPlumeRules.NextGunState(before, packM);
            m_GunStateNext[run.Run] = next;
            m_GunRunningNext[run.Run] = SnowGunPlumeRules.GunsRunning(before, cold, m_PassGunSeason);
            if (!float.IsNaN(packM))
                m_GunPackM[run.Run] = packM;
            if (next == before)
                return;
            string why = next == SnowGunPlumeRules.GunState.Running
                ? $"pack {packM:F2} m < {SnowGunPlumeRules.RestartBelowM:F2} m (stop {RunSnowRules.GunStopDepthM:F2} m - band " +
                  $"{RunSnowRules.GunRestartBandM:F2} m)"
                : $"pack {packM:F2} m" + (packM >= SnowGunPlumeRules.StopAtM
                    ? $" at the stop depth {RunSnowRules.GunStopDepthM:F2} m (built, or natural snow that deep)"
                    : $" inside the band (first measurement of the load): waiting for < {SnowGunPlumeRules.RestartBelowM:F2} m");
            Mod.Log.Info($"Snow [guns] run {run.Run.Index}:{run.Run.Version} ({run.PublicKind}) " +
                         $"{(next == SnowGunPlumeRules.GunState.Running ? "START" : "STOP")} from {before}: {why}; " +
                         $"{cold} cold texel(s) now (the guns raise, bill and plume only while running and cold)");
        }

        /// <summary>Swap the stepped maps in; ++GunGeneration when the running flags changed.</summary>
        private void PublishGuns()
        {
            bool changed = m_GunRunningNext.Count != m_GunRunning.Count;
            if (!changed)
            {
                foreach (var pair in m_GunRunningNext)
                {
                    if (!m_GunRunning.TryGetValue(pair.Key, out bool old) || old != pair.Value)
                    {
                        changed = true;
                        break;
                    }
                }
            }
            var swapState = m_GunState;
            m_GunState = m_GunStateNext;
            m_GunStateNext = swapState;
            var swapRun = m_GunRunning;
            m_GunRunning = m_GunRunningNext;
            m_GunRunningNext = swapRun;
            if (changed)
                m_GunGeneration++;
        }

        /// <summary>Forget every run's guns (a load, or the injection disabling: no run then reads running).</summary>
        private void ClearGuns()
        {
            bool had = m_GunRunning.Count > 0;
            m_GunState.Clear();
            m_GunStateNext.Clear();
            m_GunRunning.Clear();
            m_GunRunningNext.Clear();
            m_GunPackM.Clear();
            m_GunSeasonLogged = -1;   // the season line again after a load
            if (had)
                m_GunGeneration++;
        }

        /// <summary>The active (thermally on) mask texels <paramref name="run"/> claimed first in the last mask pass. False when the run
        /// was not scheduled (unbound, off the playable area, no pass yet). See the first-claim caveat above.</summary>
        internal bool TryGetRunActiveTexels(Entity run, out int texels)
        {
            return m_RunTexels.TryGetValue(run, out texels);
        }

        // Run snow, open state, the estimate. Main thread only, O(1) (a dictionary lookup, or for an estimate a handful of float operations
        // on SnowLineSystem's cached day-mean). Consumers: the score (GetRunSnow -> Multiplier, Source; RunSnowGeneration and SnowSettled
        // on its tick), the hub's stats (GetRunSnow, GetSnowSummary, RunSnowGeneration), the decal dress and the props' snow
        // (TryGetDressDepth). State / Phase are log and summary words only: the hub and the piste map use the multiplier.

        /// <summary>City-wide snow status for the hub's status line and the logs (a copy).</summary>
        internal struct SnowSummary
        {
            public RunSnowSource Source;            // None before the first pass; GatingOff; Measured if any run is; else Estimated
            public int Runs, Measured, Estimated;
            public int Open, Closed;                // by State
            public int Opening, Closing;            // by Phase
            public bool InjectionActive;            // armed and not disabled this load
            public bool DrySkip, MeasureOnly;       // episodes in progress; DrySkip = a dry one
            public bool PackedSkip;                 // a packed skip episode (no reader outside this system)
            public bool SnowVisualsOff;             // read live: SharedSettings.instance?.gameplay?.snowVisual == false
            public float SnowmakingMarginC;         // read live: SnowLineSystem.GetSnowmakingMarginC()
            public float DayMeanC;                  // read live: SnowLineSystem.GetDayMeanTemperature()
            public uint NewestSnapshotAgeFrames;    // at the last pass; uint.MaxValue when none
            public RunDressSource DressSource;
            public float DressDepthM;
        }

        /// <summary>++ whenever any run's published State, Multiplier or Source changes, a run enters or
        /// leaves the table, the gating setting flips, or the table is cleared (load, disable). The score's
        /// watcher and the hub's change signature poll it. At most one bump per evaluated pass
        /// (≤ 2 per wall-clock second) plus one per gating flip.</summary>
        internal int RunSnowGeneration => m_RunSnowGeneration;

        /// <summary>The read for score, stats, overlay, groomer and hub: the table entry if the run is scheduled, else an on-the-spot estimate
        /// from the caller's heights (Geometry.m_Bounds y) and kind, through the one estimate rule a run in the table uses too
        /// (RunSnowRules.EstimatedRunShare). A cat track (never in the table) opens like a snowmaking run: freezing + the margin while its guns
        /// make snow, on natural snow only out of the gun season or with its guns Off (DECISIONS 568, 571). Gating off: Source GatingOff, State
        /// Open, Multiplier 1 (Share and depth are still the measured or estimated values, for display). Always answers.</summary>
        internal RunSnow GetRunSnow(Entity run, RunKind kind, float bottomY, float topY)
        {
            if (!m_RunSnow.TryGetValue(run, out RunSnow s))
            {
                // not in the table (injection disabled or not armed, run unbound or off the map,
                // no pass yet) → the day-mean estimate. A run's gun value is look only (gunsOn true); a cat track's
                // switch decides. No history: the first label uses the midpoint, the multiplier is published as is.
                bool gunsOn = kind != RunKind.CatTrack || m_RunProps.CatTrackGunsOn(run);
                float share = EstimateShare(kind, bottomY, topY, gunsOn);
                // A cat track keeps its made snow while it is not in the table (from a load to the first pass): the saved share.
                if (kind == RunKind.CatTrack && EntityManager.TryGetComponent(run, out SkiTrackSnow saved))
                    share = RunSnowRules.TrackShare(share, saved.Share);
                s = new RunSnow
                {
                    Kind = kind,
                    Source = RunSnowSource.Estimated,
                    State = RunSnowRules.NextState(RunSnowState.Unknown, share),
                    Share = share,
                    Multiplier = RunSnowRules.OpenMultiplier(share),
                    MeanDepthM = float.NaN,
                    BottomY = bottomY,
                    TopY = topY,
                };
            }
            if (!m_SnowLineSystem.IsSnowGatingEnabled())
            {
                s.Source = RunSnowSource.GatingOff;
                s.State = RunSnowState.Open;
                s.Multiplier = 1f;
            }
            return s;
        }

        /// <summary>City-wide status for the hub's status line and the logs (a copy). The table counts are
        /// those of the last evaluated pass; injection state, episodes, the game's Snow visuals option, the
        /// margin, the day-mean, the gating source and the dress are read live.</summary>
        internal SnowSummary GetSnowSummary()
        {
            SnowSummary s = m_Summary;
            s.InjectionActive = m_Armed && !m_Disabled;
            s.DrySkip = m_InSkip && m_SkipKind == TopupSkip.Dry;
            s.PackedSkip = m_InSkip && m_SkipKind == TopupSkip.Packed;
            s.MeasureOnly = m_InMeasureOnly;
            // The game's Gameplay > "Snow visuals" only scales the drawn snow to 0 (SnowSystem.cs:199-206, the same read); the simulation keeps
            // running, so the measurement stays the truth and the hub adds a note.
            Game.Settings.GameplaySettings gameplay = Game.Settings.SharedSettings.instance?.gameplay;
            s.SnowVisualsOff = gameplay != null && !gameplay.snowVisual;
            s.SnowmakingMarginC = m_SnowLineSystem.GetSnowmakingMarginC();
            s.DayMeanC = m_SnowLineSystem.GetDayMeanTemperature();
            if (!m_SnowLineSystem.IsSnowGatingEnabled())
                s.Source = RunSnowSource.GatingOff;
            TryGetDressDepth(out s.DressDepthM, out s.DressSource);
            return s;
        }

        /// <summary>The depth the decal dress and the props' snow follow. Managed: MeanRunSnowDepthM (bound injected runs, last real pass
        /// this load). NaturalMeasured: the coverage-weighted mean depth over Measured natural runs (neighbor texels excluded), when no
        /// managed measurement exists this load. Natural texels never enter MeanRunSnowDepthM.</summary>
        internal bool TryGetDressDepth(out float depthM, out RunDressSource source)
        {
            if (HasRunSnowMeasurement)
            {
                depthM = MeanRunSnowDepthM;
                source = RunDressSource.Managed;
                return true;
            }
            if (m_NaturalDressValid)
            {
                depthM = m_NaturalDressDepthM;
                source = RunDressSource.NaturalMeasured;
                return true;
            }
            depthM = 0f;
            source = RunDressSource.None;
            return false;
        }

        /// <summary>True once every scheduled run has a Measured entry this load, or the injection is not active this load, or 256 sim
        /// frames have passed since the first evaluated pass of the load (a pass that idled before RefreshRunSnow counts as the first).
        /// Evaluated at the last pass; false before the first pass of the load. An empty table is vacuously "every entry Measured": true
        /// from the first refresh. The attraction chapter holds the first provider write of a load until it is true and turns its
        /// false -> true edge into a soft recompute.</summary>
        internal bool SnowSettled
        {
            get
            {
                if (!m_Armed || m_Disabled)
                    return true;
                if (!m_FirstPassSet)
                    return false;
                return m_TableAllMeasured || m_LastEvalFrame - m_FirstPassFrame >= SnowSettledFrames;
            }
        }

        private bool m_ForceNextTopupLog;
        private string m_LastIdleReason;

        // Folds the top-up lines; reset per load, flushed before a load.
        private readonly LogWindow m_TopupLog = new LogWindow(Tuning.LogWindowS);
        // The run state changes (slots: city open count, closed count, 1), the skip episodes (dry or packed) and the measure-only episodes
        // each fold through their own window; FlushWindows covers all four (before an idle line, on a load, at quit; TrailingDue after every
        // evaluated pass).
        private readonly LogWindow m_OpenLog = new LogWindow(Tuning.LogWindowS);
        private readonly LogWindow m_SkipLog = new LogWindow(Tuning.LogWindowS);
        private readonly LogWindow m_MeasureLog = new LogWindow(Tuning.LogWindowS);

        protected override void OnCreate()
        {
            base.OnCreate();
            m_SnowSystem = World.GetOrCreateSystemManaged<SnowSystem>();
            m_TerrainSystem = World.GetOrCreateSystemManaged<TerrainSystem>();
            m_SimulationSystem = World.GetOrCreateSystemManaged<SimulationSystem>();
            m_TimeSystem = World.GetOrCreateSystemManaged<TimeSystem>();   // the Snow [day] key
            m_SnowLineSystem = World.GetOrCreateSystemManaged<SnowLineSystem>();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_Ownership = World.GetOrCreateSystemManaged<SkiRunOwnershipSystem>();
            m_RunProps = World.GetOrCreateSystemManaged<SkiRunPropSystem>();
            m_ClimateSystem = World.GetOrCreateSystemManaged<ClimateSystem>();       // the season's name
            m_PlanetarySystem = World.GetOrCreateSystemManaged<PlanetarySystem>();   // the latitude's sign

            m_RunScanQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Area>(),
                    // Every run kind is a SurfacePrefab area (Game.Prefabs/SurfacePrefab.cs:18-23 adds Game.Areas.Surface); the walk skips lots,
                    // districts and the 529 map tiles that used to be tested against the prefab sets on every pass.
                    ComponentType.ReadOnly<Game.Areas.Surface>(),
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
            // Quit to desktop raises no onGamePreload, but the world's systems are destroyed while the logger still lives (LogWindow.cs header),
            // so the folded lines are written here, before any native state is disposed. Teardown must never throw: a write that fails is
            // dropped (the logger itself may be what failed).
            try
            {
                // An open skip spell ends with the reason "quit" and the counted part day is written.
                float now = UnityEngine.Time.realtimeSinceStartup;
                if (m_InSkip)
                    EndSkip("quit", now, force: true);
                WriteDayLine("quit");
                FlushWindows(now, all: true);
            }
            catch (Exception)
            {
                // nothing left to report to
            }
            // Wait before disposing the array a measure-only request writes into (vanilla BaseDataReader.Dispose, BaseDataReader.cs:148-153;
            // WaterRenderSystem.ReleaseShader, :291-296). It stays: disposing an array a readback still writes is the real hazard; it
            // writes one line when it actually waited, so a slow quit is attributable. A "Game terminated due to timeout" line at quit is
            // not from this wait (GameManager.cs:779-819; details in EngineNotes/SkiSnowInjectionSystem.md).
            try
            {
                if (m_MeasurePending && !m_MeasureRequest.done)
                {
                    long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                    m_MeasureRequest.WaitForCompletion();
                    double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 /
                                System.Diagnostics.Stopwatch.Frequency;
                    Mod.Log.Info($"Snow [measure] quit: waited {ms:F1} ms for the pending snow-map readback before " +
                                 "freeing its array");
                }
            }
            catch (Exception)
            {
                // teardown: nothing left to report to
            }
            m_MeasurePending = false;
            if (m_MeasureReadback.IsCreated)
                m_MeasureReadback.Dispose();
            if (m_Mask.IsCreated)
                m_Mask.Dispose();
            if (m_DecayMask.IsCreated)
                m_DecayMask.Dispose();
            if (m_ProtectMask.IsCreated)
                m_ProtectMask.Dispose();
            if (m_ProtectBase.IsCreated)
                m_ProtectBase.Dispose();
            if (m_Owner.IsCreated)
                m_Owner.Dispose();
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

        /// <summary>Mirror SnowSystem's own 4-frame slot (:226-229): the anchored UpdateAfter registration inherits the anchor's offset
        /// (verified against Game.UpdateSystem), so every invocation lands right after a snow tick. The 64-frame top-up cadence is
        /// self-gated in OnUpdate.</summary>
        public override int GetUpdateInterval(SystemUpdatePhase phase)
        {
            return 4;
        }

        /// <summary>A load (or leaving to the menu) writes the folded top-up lines of the city being left, so the last state before a
        /// reload is in the log. Never throws: GameSystemBase.GamePreload disables a system whose OnGamePreload throws
        /// (Game/GameSystemBase.cs:85-96, base.Enabled = false at :94; same in 1.6.2).</summary>
        protected override void OnGamePreload(Purpose purpose, GameMode mode)
        {
            base.OnGamePreload(purpose, mode);
            try
            {
                // Open episodes end with the reason "a load" (written at once, with any fold); the cost line is written now if 8-63 evaluated passes
                // ran; the counted part day is written right after the skip's end line; then every window is flushed.
                float now = UnityEngine.Time.realtimeSinceStartup;
                if (m_InSkip)
                    EndSkip("a load", now, force: true);
                WriteDayLine("the load");
                if (m_InMeasureOnly)
                    EndMeasureOnly("a load", now, force: true);
                if (!m_CostLogged && m_CostPasses >= CostMinPassesAtPreload)
                    WriteCostLine();
                FlushWindows(now, all: true);
            }
            catch (Exception e)
            {
                Mod.Log.Warn($"Snow [topup]: folded top-up, open, skip and measure lines could not be written " +
                             $"before the load ({e.GetType().Name}: {e.Message}) — non-fatal");
            }
            try
            {
                // Wait before resetting (vanilla ActiveWaterTilesHelper.Reset, ActiveWaterTilesHelper.cs:78-84); the stall happens during a load,
                // where it is invisible. Then neither snapshot may be read again before a copy succeeds in the next load.
                if (m_MeasurePending)
                {
                    m_MeasureRequest.WaitForCompletion();
                    m_MeasurePending = false;
                }
                m_MeasureValid = false;
                m_ReadbackValid = false;
            }
            catch (Exception e)
            {
                // m_MeasurePending stays as it is: no second request may target an array a request might
                // still write; the poll (or the defensive wait at load complete) settles it.
                m_MeasureValid = false;
                m_ReadbackValid = false;
                Mod.Log.Warn($"Snow [measure]: the pending snow-map readback could not be awaited before the load " +
                             $"({e.GetType().Name}: {e.Message}) — non-fatal; both snapshots invalidated");
            }
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);

            // Re-arm and reset per load, whatever the mode — leaving to the menu must
            // deactivate injection too.
            m_Armed = false;
            m_Disabled = false;
            m_ErrorLogged = false;
            m_PlanesDirty = true;      // a new city starts from cleared planes
            m_NoRunsSettled = false;
            m_BufferValidated = false;
            m_ReadbackWarned = false;
            m_InitialTopupPending = false;
            m_LastInjectedTexels = -1;
            m_LastLoweredTexels = -1;
            m_LastMaskTexels = -1;
            m_LastGateA = new int4(-1);   // no previous pass (the first one is forced anyway)
            m_LastGateB = new int4(-1);
            m_LastGateForceAt = float.NegativeInfinity;
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
            m_TopupLog.Reset();           // flushed in OnGamePreload; nothing carries over
            m_RunTexels.Clear();          // the previous city's runs are gone
            m_RunTexelsNext.Clear();
            ClearGuns();                  // likewise their guns (Unknown again: no raise or plume before a measurement)
            m_RunTexelGeneration++;

            // Every per-load field of the snapshot, skip, measure and run-table state. A measure-only request that is somehow still pending is
            // awaited first (defensive; OnGamePreload normally did it).
            try
            {
                if (m_MeasurePending)
                    m_MeasureRequest.WaitForCompletion();
                m_MeasurePending = false;
            }
            catch (Exception e)
            {
                Mod.Log.Warn($"Snow [measure]: a pending snow-map readback could not be awaited at load " +
                             $"({e.GetType().Name}: {e.Message}) — non-fatal; measure-only stays off until it settles");
            }
            m_LoadSerial++;   // a request of the city being left can never validate a snapshot of this one
            m_ReadbackValid = false;
            m_MeasureValid = false;
            m_ReadbackFrame = 0;
            m_MeasureFrame = 0;
            m_MeasureFailures = 0;
            m_MeasureStopped = false;
            m_MeasureWarned = false;
            m_MeasureFirstLogged = false;
            m_InMeasureOnly = false;
            m_MeasureEpisodeReadbacks = 0;
            m_LastReal = default;
            m_LastRealPassFrame = 0;
            m_LastPassRealTime = float.NegativeInfinity;
            m_InSkip = false;
            m_SkipEpisodeCount = 0;
            m_SkipsSinceLastReal = 0;
            m_SkipKind = TopupSkip.None;
            m_LastSkipKind = TopupSkip.None;
            m_UncalmRuns = 0;
            m_DayKey = int.MinValue;         // the first pass of this load starts a day "from the load"
            m_Protect.Clear();               // protection spells, the ledger and the per-texel baseline are per load
            m_Ledger.Clear();
            m_LedgerRuns.Clear();
            m_ProtectActiveRuns = 0;
            m_ProtectLastFrame = 0;
            m_ProtectFrameValid = false;
            m_MadeLoggedTotalM3 = 0.0;
            if (m_ProtectBase.IsCreated)
                ZeroPlane(m_ProtectBase);
            m_DayFromLoad = false;
            m_DayText = null;
            ZeroDayCounts();
            m_RunSnow.Clear();
            m_RunSnowNext.Clear();
            m_RunSnowInputs.Clear();
            m_OpenChanges.Clear();
            m_NaturalRuns.Clear();
            m_TrackMade.Clear();
            m_TrackMadeNext.Clear();
            m_TrackSaves.Clear();
            m_GunSeasonKnown = false;
            m_RunSnowGeneration++;
            m_NaturalDressValid = false;
            m_NaturalDressDepthM = 0f;
            m_LastNaturalSnapshotFrame = 0;
            m_LastNaturalFromMeasure = false;
            m_LastNaturalSignature = 0;
            m_LastNaturalInjectedHash = 0;
            m_FirstMeasuredLogged = false;
            m_LastSnapUsable = false;
            m_StaleLogged = false;
            m_LastUsableSnapFrame = 0;
            m_SourceDisabledLogged = false;
            m_FirstPassSet = false;
            m_FirstPassFrame = 0;
            m_LastEvalFrame = 0;
            m_TableAllMeasured = false;
            m_StepClampedPasses = 0;
            m_Summary = default;
            m_MultFull = m_MultPartly = m_MultZero = 0;
            m_OpenLog.Reset();
            m_SkipLog.Reset();
            m_MeasureLog.Reset();
            ResetCostRecord();
            m_LastMarginC = m_SnowLineSystem.GetSnowmakingMarginC();
            m_LastGating = m_SnowLineSystem.IsSnowGatingEnabled();

            // HARD GATE: GameMode.Game only — never Editor. SnowSystem also runs in
            // EditorSimulation (SystemOrder.cs:604) but we do not register there, and this
            // gate is the second lock: a map author must never save our snow into a map.
            if (mode != GameMode.Game)
                return;

            try
            {
                ResolveRunPrefabs();
                // armed when ANY of the four run prefab sets is non-empty (a natural-only install
                // still measures its runs).
                if (m_SnowmakingPrefabs.Count == 0 && m_GroomedPrefabs.Count == 0 &&
                    m_NightPrefabs.Count == 0 && m_NaturalPrefabs.Count == 0 && m_TrackMeasurePrefabs.Count == 0)
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
                             $"freezing+{m_LastMarginC:F1} C (Options: Snowmaking margin), groomed " +
                             $"{Tuning.GroomedTargetDepthM:F2} m where <= " +
                             $"freezing+{Tuning.GroomedMarginC:F1} C; ramp " +
                             $"+{Tuning.RampPerTopupM:F3} m/top-up x{Tuning.RampRateMin:F2}-" +
                             $"{Tuning.RampRateMin + Tuning.RampRateSpan:F2} noisy (no pop-in); " +
                             $"too-warm texels decay " +
                             $"-{Tuning.DecayPerTopupM:F3} m/top-up (bottom-up melt); top-up every " +
                             $"{Tuning.TopupIntervalFrames} sim frames + once on first sim tick, at most one per " +
                             $"{RunSnowRules.MinPassIntervalS:F2} s wall clock (ramp and decay scaled by elapsed " +
                             $"frames / {Tuning.TopupIntervalFrames}, cap x{RunSnowRules.MaxStepScale:F0}); dry skip " +
                             "when no run texel is cold and the footprint was bare at the last real pass; packed skip " +
                             "when no run texel is cold, the snowpack holds managed melt (map mean > " +
                             $"{RunSnowRules.PackGuardMeanDepthM:F2} m), the runs held >= " +
                             $"{RunSnowRules.PackedSkipMinRunDepthM:F2} m and every measured run's snow share was >= " +
                             $"{RunSnowRules.CalmOpenShare * 100:F0} % or < {RunSnowRules.CalmClosedShare * 100:F0} % at the " +
                             "last real pass; either way a real pass every " +
                             $"{RunSnowRules.ForcedRealPassFrames} sim frames; per-run snow measured " +
                             "from the last snow-map snapshot (skiable >= " +
                             $"{RunSnowRules.SkiableDepthM:F2} m ({RunSnowRules.ExpertNaturalSkiableDepthM:F2} m on a natural run rated black or double black) or the texel's own fringe target; open >= " +
                             $"{RunSnowRules.OpenAtShare * 100f:F0} %, closed < {RunSnowRules.CloseBelowShare * 100f:F0} %, " +
                             "multiplier ramps between); measure-only async readback every " +
                             $"{RunSnowRules.MeasureIntervalFrames} sim frames when no write pass is due; " +
                             $"mask supersample {Tuning.SupersampleGrid}x{Tuning.SupersampleGrid}, " +
                             "fringe contained to run footprints; top-up lines at most one per " +
                             $"LogWindowS={Tuning.LogWindowS:0} s wall clock (a burst is folded into one summary; " +
                             $"a gate change is written at once; unchanged, a heartbeat every HeartbeatS={Tuning.HeartbeatS:0} s); " +
                             $"snow & season gating {(m_LastGating ? "on" : "off")}; " +
                             $"{m_SnowmakingPrefabs.Count} snowmaking + {m_GroomedPrefabs.Count} " +
                             $"groomed + {m_NightPrefabs.Count} night + {m_NaturalPrefabs.Count} natural run " +
                             "prefab(s) watched (natural: measured only); " +
                             // The numbers of the rules, every one a named constant in RunSnowRules.
                             $"T09: gun gate margin default {RunSnowRules.DefaultSnowmakingMarginC:F1} C (now {m_LastMarginC:F1}), gun target " +
                             $"{RunSnowRules.GunTargetDepthM:F2} m / groomed {RunSnowRules.GroomedTargetDepthM:F2} m (open bar " +
                             $"{RunSnowRules.SkiableDepthM:F2} m), slowest texel reaches the gun target in " +
                             $"{RunSnowRules.WindowsToGameHours(RunSnowRules.GunWindowsToReach(RunSnowRules.GunTargetDepthM, -3f, RunSnowRules.DefaultSnowmakingMarginC, RunSnowRules.RampRateMin)):F1} " +
                             "game h at -3 C; warm-spell protection of gun runs: budget " +
                             $"{RunSnowRules.ProtectBudgetHours:F0} game h (x{RunSnowRules.ProtectSpringFactor:F1} in spring, " +
                             $"x{RunSnowRules.ProtectNorthFactor:F1} north faces, x{RunSnowRules.ProtectSouthFactor:F1} south faces), none above " +
                             $"{RunSnowRules.ProtectMaxLocalC:F0} C at the run, +{RunSnowRules.ProtectRaisePerTopupM:F3} m/top-up toward the " +
                             $"pre-spell depth, budget refills after {RunSnowRules.ProtectRearmColdHours:F0} game h of cold; natural runs none; " +
                             "snow made / held ledger per run (Snow [made] once per game day, catalog DUMP row; not saved); " +
                             // the guns stop and restart (SnowGunPlumeRules).
                             $"T46: the guns of a snowmaking or night run build to {RunSnowRules.GunStopDepthM:F2} m and STOP there " +
                             $"(natural snow that deep included), restart below {SnowGunPlumeRules.RestartBelowM:F2} m; only " +
                             "running guns raise, bill and plume; a run's guns wait for the first measurement of the load");
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
                // A bool test, and one property read only while a measure-only request is in flight (a few rendered frames per 1,024 sim frames).
                if (m_MeasurePending)
                    PollMeasureReadback(frame);
                // Resort-present bit (ResortPresence header): a city whose last pass found no run polygon and that has no sign, run or lift of ours
                // does no work here: no planes, no area walk, no sync.
                if (m_NoRunsSettled && !m_MeasurePending && !ResortPresence.Present)
                    return;
                if (!m_InitialTopupPending &&
                    frame - m_LastTopupFrame < (uint)Tuning.TopupIntervalFrames)
                    return;
                float now = UnityEngine.Time.realtimeSinceStartup;
                // At most one evaluated pass per MinPassIntervalS of wall clock. A postponed pass re-checks in 4 sim frames (a float compare); the
                // frames keep counting into `elapsed`. The first pass after a load is exempt.
                if (!m_InitialTopupPending && now - m_LastPassRealTime < RunSnowRules.MinPassIntervalS)
                    return;
                uint elapsed = m_InitialTopupPending ? (uint)Tuning.TopupIntervalFrames : frame - m_LastTopupFrame;
                m_InitialTopupPending = false;
                m_LastTopupFrame = frame;
                m_LastPassRealTime = now;

                Topup(frame, elapsed, now);
                // Once per load, after the CostPasses-th evaluated pass (its real / skip classification is known by now).
                if (!m_CostLogged && m_CostPasses >= CostPasses)
                    WriteCostLine();
                // the end of a burst is written at most LogWindowS after the last line, on every
                // evaluated pass that offered nothing (never silent).
                FlushWindows(now, all: false);
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
                // A pending measure-only request is left to complete (the preload and destroy paths await it); the run table is cleared (every run
                // is then estimated) and the generation bumped.
                ClearRunSnow();
            }
        }

        // The whole hot path: gate + rasterize mask, validate buffer (once), blocking readback, CPU max-merge, upload via the vanilla Load
        // kernel. Every 64 sim frames. The readback is blocking on purpose: the merge writes the full texture back (the vanilla Load
        // kernel has no sub-rect parameters), so a stale async snapshot would revert natural snowfall map-wide on upload. Same-frame
        // atomicity holds: GameSimulation systems run sequentially on the main thread and SnowSystem is the sole gameplay-time writer.
        // The "step N" comments below refer to the pass flow in EngineNotes/SkiSnowInjectionSystem.md (Near Topup).
        private void Topup(uint frame, uint elapsedFrames, float now)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            double tMask = 0, tReadback = 0, tMerge = 0;
            m_NoRunsSettled = false;

            // The first evaluated pass of the load starts SnowSettled's 256-frame clock, also when this pass idles below, before any refresh.
            if (!m_FirstPassSet)
            {
                m_FirstPassSet = true;
                m_FirstPassFrame = frame;
            }
            m_LastEvalFrame = frame;
            // Every evaluated pass counts toward the Snow [day] line, idle exits included.
            AdvanceSnowDay();
            m_DayPasses++;

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

            EnsureSnapshotBuffers();
            PollMeasureReadback(frame);

            float ambient = m_SnowLineSystem.GetAmbientTemperature();
            float freezing = m_SnowLineSystem.GetFreezingTemperature();
            float margin = m_SnowLineSystem.GetSnowmakingMarginC();   // the margin, once per pass
            NoteSettingChanges(margin);
            // this pass's season and hemisphere for the protection budget (a string compare and a float read).
            if (m_GunSeasonLogged < 0)
                SkiClimateCalendar.WinterWindow(m_ClimateSystem, m_PrefabSystem, out m_GunWinterStart, out m_GunWinterEnd);
            m_PassSpring = RunSnowRules.IsSpring(m_ClimateSystem.currentSeasonName, m_TimeSystem.normalizedDate, m_GunWinterEnd);
            // the gun season (around this climate's winter; a vanilla winter / summer name decides first). Out of it the guns raise,
            // bill and plume nothing; one line per change.
            m_PassGunSeason = SnowGunPlumeRules.InGunSeason(m_ClimateSystem.currentSeasonName, m_TimeSystem.normalizedDate, m_GunWinterStart, m_GunWinterEnd);
            m_GunSeasonKnown = true;
            if (m_GunSeasonLogged != (m_PassGunSeason ? 1 : 0))
            {
                Mod.Log.Info($"Snow [guns] season {(m_PassGunSeason ? "IN" : "OUT")} (season '{m_ClimateSystem.currentSeasonName}', date " +
                             $"{m_TimeSystem.normalizedDate:F3}, winter {m_GunWinterStart:F3} to {m_GunWinterEnd:F3}): snow guns run " +
                             $"{SnowGunPlumeRules.MonthsBeforeWinter:F0} months before winter to {SnowGunPlumeRules.MonthsAfterWinter:F0} after (T135)" +
                             (m_GunSeasonLogged < 0 ? " — first this load" : ""));
                m_GunSeasonLogged = m_PassGunSeason ? 1 : 0;
            }
            m_PassNorthSign = m_PlanetarySystem.latitude < 0f ? -1f : 1f;

            SnapshotChoice snap = ChooseSnapshot(frame);
            MaskStats stats = BuildMask(playOffset, playSize, ambient, freezing, margin, snap, frame);
            // Publish the per-type active mask before ANY early-out below: "all gates
            // off" and "no runs placed" both mean zero billable operations, and the
            // upkeep system must see that zero, not a stale count.
            float2 texelDims = playSize / TexSize;
            TexelAreaM2 = texelDims.x * texelDims.y;
            SnowmakingActiveTexels = stats.SnowmakingMaskTexels;
            GroomedActiveTexels = stats.GroomedMaskTexels;
            NightActiveTexels = stats.NightMaskTexels;
            HasMaskMeasurement = true;
            // Refresh the per-run table on every evaluated pass, before any early return, so a natural-only city refreshes too. Inside the mask
            // phase timer.
            RefreshRunSnow(frame, snap, freezing, margin, now);
            tMask = timer.Elapsed.TotalMilliseconds;
            NoteEvaluatedPass(now, elapsedFrames, stats);

            int totalRuns = stats.SnowmakingTotal + stats.GroomedTotal + stats.NightTotal;
            if (totalRuns == 0)
            {
                EndSkipIfOpen(stats, now);
                // A natural-only city reads the snow map through the non-blocking measure-only readback (also ends an open episode when no natural
                // run is left).
                MaybeRequestMeasure(frame, "only natural runs are placed", stats.NaturalScheduled, now);
                if (stats.NaturalScheduled > 0)
                    SetIdle($"no injected run placed — {stats.NaturalScheduled} natural run(s) and cat track(s) measured only " +
                            $"(async snow-map readback every {RunSnowRules.MeasureIntervalFrames} sim frames)");
                else
                {
                    SetIdle("no run polygons placed");
                    m_NoRunsSettled = stats.NaturalTotal == 0 && stats.UnboundRuns == 0;
                }
                return;
            }
            // This early-out must not fire whenever all gates are off: that is exactly when managed decay must run. Idle only when there is
            // neither anything to inject nor anything to decay; a gates-off pass with decay coverage proceeds to the merge (0 raised, N lowered).
            // A stopped gun run's cold texels keep the pass real (a readback every window): the guns restart from the pack this snapshot
            // measures, so it must stay fresh.
            if (stats.MaskTexels == 0 && stats.DecayTexels == 0 && stats.ProtectedTexels == 0 && stats.StoppedTexels == 0)
            {
                EndSkipIfOpen(stats, now);
                MaybeRequestMeasure(frame, "no managed texel to top up", stats.NaturalScheduled, now);
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

            // Dry skip: no texel cold enough now, the last real pass found the whole footprint warm and bare, the footprint is the same, and
            // the forced real pass is not due, so a real pass would provably raise nothing and lower nothing it is owed. No readback, merge or
            // upload; a measure-only request is normally a no-op here (the forced real pass is the 1,024-frame snapshot).
            // Packed skip: no texel cold now, the last real pass raised and lowered nothing and left the pack guard on, the runs held >=
            // PackedSkipMinRunDepthM, every measured share is calm, the same footprint, the forced pass not due. Dry is tested first and
            // ignores the share guard. The share guard is moot with gating off (GetRunSnow then answers Open and multiplier 1 to every reader);
            // m_LastGating was refreshed in step 3 (NoteSettingChanges).
            uint sinceReal = frame - m_LastRealPassFrame;
            bool calm = !m_LastGating || m_UncalmRuns == 0;
            // a run in an Active protection spell needs a real pass every window (the game melts, we put it back):
            // never skip while one is on (m_ProtectActiveRuns was set by this pass's RefreshRunSnow).
            TopupSkip skip = m_ProtectActiveRuns > 0 ? TopupSkip.None : RunSnowRules.SkipKind(m_LastReal,
                stats.ColdTexels, stats.ContainedTexels, stats.FootprintHash, sinceReal, calm);
            if (skip != TopupSkip.None)
            {
                NoteSkip(skip, stats, ambient, now, tMask);
                MaybeRequestMeasure(frame, skip == TopupSkip.Dry ? "dry skip" : "packed skip", stats.NaturalScheduled, now);
                m_LastIdleReason = null;   // not idle: the next idle spell logs again
                return;
            }
            // Step 11: a forced real pass (the 1,024-frame check) keeps the episode open unless the record it makes would not skip the same way
            // (step 15); any other reason to do a real pass ends it now, with the first failing term. "Forced" only when the frame term is the
            // only failing term, so a pass where the frame term is due and a texel turned cold (or the footprint changed, or a measured share
            // left the calm band) ends the episode here with that reason, not at step 15.
            bool forced = m_InSkip && m_ProtectActiveRuns == 0 && RunSnowRules.IsForcedCheck(m_LastReal, stats.ColdTexels,
                stats.ContainedTexels, stats.FootprintHash, sinceReal, calm);
            TopupSkip forcedKind = forced ? m_SkipKind : TopupSkip.None;
            if (m_InSkip && !forced)
                EndSkip(SkipEndReason(stats, calm), now, force: false);
            if (m_InMeasureOnly)
                EndMeasureOnly("a write pass is due", now, force: false);

            EnsureGpuBuffers();

            // Blocking readback: the game's own Serialize pattern (SnowSystem.cs:250-251). Same-frame read/merge/upload closes the async-latency
            // window entirely.
            AsyncGPUReadbackRequest request =
                AsyncGPUReadback.RequestIntoNativeArray(ref m_Readback, snowDepth);
            request.WaitForCompletion();
            if (request.hasError)
            {
                m_ReadbackValid = false;   // the array content is undefined
                if (!m_ReadbackWarned)
                {
                    m_ReadbackWarned = true;
                    Mod.Log.Warn("Snow [topup]: GPU readback failed — top-up skipped, retried " +
                                 "next window (warned once per load)");
                }
                return;
            }
            m_ReadbackValid = true;        // the snapshot later passes measure from
            m_ReadbackFrame = frame;
            tReadback = timer.Elapsed.TotalMilliseconds;

            // CPU max-merge (MergeRowsJob). R: r = max(r, target x coverage): idempotent, monotone, never reduces. G: exact ushort round-trip
            // (k/65535 re-quantizes to k), water is untouched. Deep winter: natural depth already exceeds every target, injected stays 0, and
            // the upload below is skipped. Ramp semantics: coarse-lobe rate noise + chill factor, deterministic per texel (see the job body).
            // The pack guard (RunSnowRules.PackGuard, also read by the packed skip) uses the last pass's map mean (one-window lag, harmless):
            // while a winter pack covers the world, decay stands down and runs melt with the landscape, never ahead of it. The mean it read is
            // kept in packMean for the top-up line: m_LastGlobalMeanDepthM gets this pass's mean below, before the line is built.
            float packMean = m_LastGlobalMeanDepthM;
            bool packGuard = RunSnowRules.PackGuard(packMean);
            int injected = 0, lowered = 0, heldTexels = 0;
            double globalDepthSum = 0.0, runDepthSum = 0.0;
            int runTexels = 0;
            // Ramp and decay steps scale with the frames since the last evaluated pass (a skip spell, which provably had nothing to do, does not
            // inflate it), clamped 1..4. At 1x and 2x the floor never binds, elapsed is 64, stepScale is exactly 1f and x * 1f == x: the merge
            // is bit-identical to the unscaled one. The merge's min(target, ...) / max(0, ...) prevent any overshoot.
            float stepScale = RunSnowRules.StepScale(elapsedFrames);
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
                    DecayNorm = Tuning.DecayPerTopupM / SnowHeightScaleM * stepScale,
                    RampNormBase = Tuning.RampPerTopupM / SnowHeightScaleM * stepScale,
                    ProtectMask = m_ProtectMask,
                    ProtectBase = m_ProtectBase,
                    ProtectRaiseNorm = RunSnowRules.ProtectRaisePerTopupM / SnowHeightScaleM * stepScale,
                }.ScheduleParallel(TexSize, RowBatch, default).Complete();

                for (int r = 0; r < TexSize; r++)
                {
                    MergeRowPartial p = mergePartials[r];
                    injected += p.Injected;
                    lowered += p.Lowered;
                    heldTexels += p.Held;
                    runTexels += p.RunTexels;
                    globalDepthSum += p.GlobalDepthSum;
                    runDepthSum += p.RunDepthSum;
                }
            }
            finally
            {
                mergePartials.Dispose();
            }
            if (injected + heldTexels > 0)
                AccumulateLedger();   // snow made / held per run, from this merge
            tMerge = timer.Elapsed.TotalMilliseconds;
            m_LastGlobalMeanDepthM = (float)(globalDepthSum / TexelCount) * SnowHeightScaleM;
            MeanRunSnowDepthM = runTexels > 0
                ? (float)(runDepthSum / runTexels) * SnowHeightScaleM : 0f;
            HasRunSnowMeasurement = true;

            // What the next pass's skip predicates compare against; also Lowered, the pack guard the next merge applies (from this pass's map
            // mean, written just above) and the run mean (the dress depth).
            m_LastReal = new DryPass
            {
                Valid = true,
                ColdTexels = stats.ColdTexels,
                Injected = injected,
                RunDepthSum = runDepthSum,
                ContainedTexels = stats.ContainedTexels,
                FootprintHash = stats.FootprintHash,
                Lowered = lowered,
                PackGuardNext = RunSnowRules.PackGuard(m_LastGlobalMeanDepthM),
                RunMeanDepthM = MeanRunSnowDepthM,
            };
            m_LastRealPassFrame = frame;
            // Step 15: a forced check ends its episode when the record it just made would not let the next pass skip the same way (frame term
            // ignored; the share guard as of this pass, which measured the old snapshot: a share this readback moves out of the calm band ends
            // the spell at the next pass's step 11). For a dry episode this is exactly "ends when runDepthSum > 0 || injected > 0" (forced
            // implies no cold texel, so injected is 0 and the record is dry iff runDepthSum == 0).
            if (forced)
            {
                TopupSkip next = RunSnowRules.SkipKind(m_LastReal, stats.ColdTexels, stats.ContainedTexels,
                    stats.FootprintHash, 0u, calm);
                if (next != forcedKind)
                    EndSkip(ForcedEndReason(forcedKind, next), now, force: false);
            }

            if (injected + lowered + heldTexels > 0)
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
            m_LastIdleReason = null;
            double passTotalMs = timer.Elapsed.TotalMilliseconds;
            NoteRealPass(tMask, tReadback - tMask, tMerge - tReadback, passTotalMs - tMerge);
            m_DayReal++;                     // the Snow [day] line
            m_DayRealMs += passTotalMs;
            if (forced)
                m_DayForced++;
            int skipsBefore = m_SkipsSinceLastReal;
            m_SkipsSinceLastReal = 0;

            // A gate change against the previous pass (a run's gate opening or closing, a run added, removed, bound or unbound, the mask
            // emptying or filling) is the event a reader looks for, so it is forced like a load or an idle spell, but at most once per
            // LogWindowS; a second change inside the window folds.
            int4 gateA = new int4(stats.SnowmakingActive, stats.SnowmakingTotal, stats.GroomedActive, stats.GroomedTotal);
            int4 gateB = new int4(stats.NightActive, stats.NightTotal, stats.UnboundRuns, stats.MaskTexels == 0 ? 1 : 0);
            bool gateChanged = math.any(gateA != m_LastGateA) || math.any(gateB != m_LastGateB);
            m_LastGateA = gateA;
            m_LastGateB = gateB;
            bool log = m_ForceNextTopupLog || gateChanged ||
                       injected != m_LastInjectedTexels ||
                       lowered != m_LastLoweredTexels ||
                       stats.MaskTexels != m_LastMaskTexels ||
                       m_TopupLog.QuietFor(now, Tuning.HeartbeatS);   // unchanged: wall-clock heartbeat
            if (log)
            {
                bool force = m_ForceNextTopupLog;
                m_ForceNextTopupLog = false;
                if (!force && gateChanged && !(now - m_LastGateForceAt < Tuning.LogWindowS))
                {
                    force = true;
                    m_LastGateForceAt = now;
                }
                double passMs = passTotalMs;
                string body = $"{injected} texel(s) raised of {stats.MaskTexels} " +
                              $"masked, {lowered} lowered by managed melt " +
                              $"(-{Tuning.DecayPerTopupM:F3} m/top-up on gate-off runs)" +
                              // the protected warm texels this pass held, and put back (only while a spell is on).
                              (stats.ProtectedTexels > 0 || heldTexels > 0
                                  ? $", {heldTexels} re-raised of {stats.ProtectedTexels} held by warm-spell protection ({m_ProtectActiveRuns} run(s))"
                                  : "") +
                              // The pack guard, with the map mean the merge read.
                              (packGuard
                                  ? $", pack guard on (map mean {packMean:F3} m above " +
                                    $"{RunSnowRules.PackGuardMeanDepthM:F2} m: no managed melt)"
                                  : "") +
                              $" ({stats.SnowmakingActive}/{stats.SnowmakingTotal} snowmaking " +
                              $"@ {Tuning.SnowmakingTargetDepthM:F2} m, " +
                              $"{stats.GroomedActive}/{stats.GroomedTotal} groomed " +
                              $"@ {Tuning.GroomedTargetDepthM:F2} m, " +
                              $"{stats.NightActive}/{stats.NightTotal} night, " +
                              $"{stats.Triangles} triangle(s)), " +
                              $"ambient {ambient:F1} C, skipped: " +
                              $"{stats.SnowmakingTotal - stats.SnowmakingActive} snowmaking + " +
                              $"{stats.GroomedTotal - stats.GroomedActive} groomed + " +
                              $"{stats.NightTotal - stats.NightActive} night run(s) fully " +
                              "below the line" +
                              (stats.UnboundRuns > 0
                                  ? $" (incl. {stats.UnboundRuns} unbound — no gate in range)"
                                  : "") +
                              $", snow line ~{m_SnowLineSystem.GetSnowLineElevation():F0} m, " +
                              $"{passMs:F1} ms " +
                              $"(mask {tMask:F1} + readback {tReadback - tMask:F1} + merge " +
                              $"{tMerge - tReadback:F1} + upload " +
                              $"{passMs - tMerge:F1})" +
                              (injected == 0 && lowered == 0
                                  ? " (no-op: nothing to raise or melt)" : "") +
                              // The step scale, the skip (dry or packed), and the runs' snow state: by label / phase, and by multiplier (the numbers the hub's
                              // Runs view shows).
                              (stepScale != 1f
                                  ? $", step x{stepScale:F2} ({elapsedFrames} sim frames since the last pass)" : "") +
                              // The skip's kind, and the spell count while the spell goes on.
                              (forced ? $", forced check of the {KindWord(forcedKind)} skip" : "") +
                              (skipsBefore > 0
                                  ? $", after {skipsBefore} {KindWord(m_LastSkipKind)}-skipped pass(es)" +
                                    (m_InSkip ? $" ({m_SkipEpisodeCount} this spell)" : "")
                                  : "") +
                              $"; runs: {m_Summary.Open - m_Summary.Closing} open, {m_Summary.Closing} closing, " +
                              $"{m_Summary.Opening} opening, {m_Summary.Closed - m_Summary.Opening} closed " +
                              $"({m_Summary.Estimated} estimated); by multiplier (the hub's marks): " +
                              $"{m_MultFull} at 100 %, {m_MultPartly} partly, {m_MultZero} at 0 %";
                // The first line after a quiet window (or after a load or an idle spell: force) is written as before; the rest of a burst is folded.
                switch (m_TopupLog.Offer(now, force, body, injected, lowered, passMs))
                {
                    case LogWindow.Verdict.Plain:
                        Mod.Log.Info("Snow [topup] " + body);
                        break;
                    case LogWindow.Verdict.Summary:
                        WriteTopupSummary(now);
                        break;
                }
            }
            else if (m_TopupLog.TrailingDue(now))
            {
                WriteTopupSummary(now);   // the end of a burst: never silent
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
            // The folded top-ups before the idle spell come first, so the log reads in order (and the folded open / skip / measure lines).
            FlushWindows(UnityEngine.Time.realtimeSinceStartup, all: true);
            Mod.Log.Info($"Snow [idle]: {reason} — nothing injected; re-evaluated every " +
                         $"{Tuning.TopupIntervalFrames} sim frames");
        }

        /// <summary>Writes the folded lines of the four windows: every window with folded events when <paramref name="all"/> (before an idle
        /// line, on a load, at quit), else only those whose window has passed (TrailingDue, after every evaluated pass).</summary>
        private void FlushWindows(float now, bool all)
        {
            if (m_TopupLog.Count > 0 && (all || m_TopupLog.TrailingDue(now)))
                WriteTopupSummary(now);
            if (m_OpenLog.Count > 0 && (all || m_OpenLog.TrailingDue(now)))
                WriteOpenSummary(now);
            if (m_SkipLog.Count > 0 && (all || m_SkipLog.TrailingDue(now)))
                WriteEpisodeSummary(m_SkipLog, "Snow [skip]", "skip", now);   // either kind
            if (m_MeasureLog.Count > 0 && (all || m_MeasureLog.TrailingDue(now)))
                WriteEpisodeSummary(m_MeasureLog, "Snow [measure]", "measure-only", now);
        }

        /// <summary>The folded run state changes: how many, over how long, the city's open and closed counts (min-max), then the last folded
        /// line.</summary>
        private void WriteOpenSummary(float now)
        {
            Mod.Log.Info($"Snow [open] {m_OpenLog.Count} more run state change(s) in {m_OpenLog.SpanS(now):F0} s, folded " +
                         $"(one line per LogWindowS={Tuning.LogWindowS:0} s): open {m_OpenLog.Range(0, "F0")}, " +
                         $"closed {m_OpenLog.Range(1, "F0")} — last: " + m_OpenLog.LastText);
            m_OpenLog.Wrote(now);
        }

        /// <summary>The fold summary of the skip and measure-only episode lines, in the open summary's pattern: count, span, then the last
        /// folded line.</summary>
        private static void WriteEpisodeSummary(LogWindow window, string prefix, string what, float now)
        {
            Mod.Log.Info($"{prefix} {window.Count} more {what} line(s) in {window.SpanS(now):F0} s, folded " +
                         $"(one line per LogWindowS={Tuning.LogWindowS:0} s) — last: " + window.LastText);
            window.Wrote(now);
        }

        /// <summary>Offers one episode line to its window: written at once after a quiet window (or when forced: a load), else folded; a
        /// passed window writes the summary.</summary>
        private static void OfferEpisodeLine(LogWindow window, string prefix, string what, string body, bool force,
            float now)
        {
            switch (window.Offer(now, force, body, 1.0))
            {
                case LogWindow.Verdict.Plain:
                    Mod.Log.Info(prefix + " " + body);
                    break;
                case LogWindow.Verdict.Summary:
                    WriteEpisodeSummary(window, prefix, what, now);
                    break;
            }
        }

        /// <summary>One line for the folded top-ups: how many, over how long, the raised / lowered / pass-ms ranges, then the last folded
        /// pass's full line.</summary>
        private void WriteTopupSummary(float now)
        {
            Mod.Log.Info($"Snow [topup] {m_TopupLog.Count} more top-up line(s) in {m_TopupLog.SpanS(now):F0} s, folded " +
                         $"(one line per LogWindowS={Tuning.LogWindowS:0} s): raised {m_TopupLog.Range(0, "F0")}, " +
                         $"lowered {m_TopupLog.Range(1, "F0")} texel(s), pass {m_TopupLog.Range(2, "F1")} ms — last: " +
                         m_TopupLog.LastText);
            m_TopupLog.Wrote(now);
        }

        // Bookkeeping: snapshots, the measure-only readback, the per-run table, the skip (dry / packed) and measure-only episodes, settings
        // changes, the disable path, the cost record and the Snow [day] count. Main thread only.

        /// <summary>The newest snapshot that is valid this load, not being written, and at most MaxSnapshotAgeFrames old (uint arithmetic,
        /// wrap-safe). Ties go to m_Readback.</summary>
        private SnapshotChoice ChooseSnapshot(uint frame)
        {
            bool r = m_ReadbackValid && frame - m_ReadbackFrame <= RunSnowRules.MaxSnapshotAgeFrames;
            bool m = m_MeasureValid && !m_MeasurePending &&
                     frame - m_MeasureFrame <= RunSnowRules.MaxSnapshotAgeFrames;
            if (m && (!r || frame - m_MeasureFrame < frame - m_ReadbackFrame))
                return new SnapshotChoice { Usable = true, FromMeasure = true, Frame = m_MeasureFrame };
            if (r)
                return new SnapshotChoice { Usable = true, FromMeasure = false, Frame = m_ReadbackFrame };
            return default;
        }

        /// <summary>A non-blocking measure-only readback into m_MeasureReadback, only on an evaluated pass without a real pass, only with
        /// natural runs to measure, only when no snapshot is younger than MeasureIntervalFrames, one request in flight. In a skip episode
        /// (dry or packed) the forced real pass refreshes m_Readback every 1,024 frames, so `fresh` holds and no request is made: a packed
        /// spell adds no GPU work. Called only after BuildMask's chains completed (no job reads the array). With no natural run an open
        /// measure-only episode ends.</summary>
        private void MaybeRequestMeasure(uint frame, string reason, int runs, float now)
        {
            if (runs == 0)
            {
                if (m_InMeasureOnly)
                    EndMeasureOnly("no natural run to measure", now, force: false);
                return;
            }
            if (m_MeasureStopped || m_MeasurePending)
                return;
            bool fresh = (m_ReadbackValid && frame - m_ReadbackFrame < RunSnowRules.MeasureIntervalFrames) ||
                         (m_MeasureValid && frame - m_MeasureFrame < RunSnowRules.MeasureIntervalFrames);
            if (fresh)
                return;
            RenderTexture snowDepth = m_SnowSystem.SnowDepth;
            if (snowDepth == null)
                return;
            m_MeasureValid = false;                                 // its content is about to be overwritten
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            m_MeasureRequest = AsyncGPUReadback.RequestIntoNativeArray(ref m_MeasureReadback, snowDepth);
            m_MeasureRequestMs = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 /
                                 System.Diagnostics.Stopwatch.Frequency;
            m_MeasurePending = true;
            m_MeasureRequestFrame = frame;
            m_MeasureRequestRenderFrame = UnityEngine.Time.frameCount;
            m_MeasureRequestLoad = m_LoadSerial;
            if (m_CostThisPass)
            {
                m_CostMeasureRequests++;
                m_CostMeasureRequestMs += m_MeasureRequestMs;
            }
            if (!m_InMeasureOnly)
            {
                m_InMeasureOnly = true;
                m_MeasureEpisodeReadbacks = 0;
                m_MeasureEpisodeLatMin = int.MaxValue;
                m_MeasureEpisodeLatMax = -1;
                OfferEpisodeLine(m_MeasureLog, "Snow [measure]", "measure-only",
                    $"measure-only ({reason}): async snow-map readback every {RunSnowRules.MeasureIntervalFrames} " +
                    $"sim frames for {runs} run(s), no main-thread wait", force: false, now);
            }
        }

        /// <summary>Vanilla's poll order: hasError, then done (TerrainSystem.cs:3571-3592); no copy is needed, the array is the snapshot. A
        /// failure WARNs once per load and is retried at the next pass; MeasureFailLimit failures in a row stop measure-only for the load
        /// (TerrainSystem.cs:3595-3603). m_MeasureFrame is the sim frame of the request: the GPU copy is queued behind that frame's work, so
        /// the data is at least that new (unverified ordering; used as a lower bound only).</summary>
        private void PollMeasureReadback(uint frame)
        {
            if (!m_MeasurePending)
                return;
            if (m_MeasureRequest.hasError)
            {
                m_MeasurePending = false;
                m_MeasureValid = false;
                m_MeasureFailures++;
                if (!m_MeasureWarned)
                {
                    m_MeasureWarned = true;
                    Mod.Log.Warn($"Snow [measure]: async snow-map readback failed ({m_MeasureFailures} in a row) — " +
                                 "retried at the next pass; runs without a write pass stay estimated (warned once per load)");
                }
                if (m_MeasureFailures >= RunSnowRules.MeasureFailLimit && !m_MeasureStopped)
                {
                    m_MeasureStopped = true;
                    Mod.Log.Warn($"Snow [measure]: async snow-map readback failed {RunSnowRules.MeasureFailLimit} times " +
                                 "in a row — measure-only off for this load; runs without a write pass stay estimated");
                }
                return;
            }
            if (!m_MeasureRequest.done)
                return;
            m_MeasurePending = false;
            // A request of an earlier load (left pending only when a wait at load threw) never becomes a snapshot of this load.
            if (m_MeasureRequestLoad != m_LoadSerial)
                return;
            m_MeasureValid = true;
            m_MeasureFrame = m_MeasureRequestFrame;   // the snapshot shows the texture as of the request
            m_MeasureFailures = 0;
            int latency = UnityEngine.Time.frameCount - m_MeasureRequestRenderFrame;   // observed, rendered frames
            m_MeasureEpisodeReadbacks++;
            if (latency < m_MeasureEpisodeLatMin)
                m_MeasureEpisodeLatMin = latency;
            if (latency > m_MeasureEpisodeLatMax)
                m_MeasureEpisodeLatMax = latency;
            if (!m_CostLogged)
            {
                if (latency < m_CostLatencyMin)
                    m_CostLatencyMin = latency;
                if (latency > m_CostLatencyMax)
                    m_CostLatencyMax = latency;
            }
            if (!m_MeasureFirstLogged)
            {
                m_MeasureFirstLogged = true;
                Mod.Log.Info($"Snow [measure] first async readback done: {latency} rendered frame(s) after the request " +
                             $"(observed), request call {m_MeasureRequestMs:F3} ms");
            }
        }

        /// <summary>The day-mean estimate for a run not in the table (RunSnowRules.EstimatedRunShare, the rule RefreshRunSnow uses for a run it
        /// could not measure): freezing + the margin while a snowmaking run, night run or cat track can make snow (this pass's gun season, and
        /// <paramref name="gunsOn"/>: a cat track's switch, true for a run), else freezing; groomed freezing + 0. CatTrackGunsCanRun asks with
        /// the guns on, as the guns' own reach.</summary>
        private float EstimateShare(RunKind kind, float bottomY, float topY, bool gunsOn = true)
        {
            return RunSnowRules.EstimatedRunShare(kind, bottomY, topY, m_SnowLineSystem.GetDayMeanTemperature(),
                m_SnowLineSystem.GetFreezingTemperature(), m_SnowLineSystem.GetSnowmakingMarginC(), m_SnowLineSystem.GetBaseHeight(),
                m_SnowLineSystem.GetLapsePerM(), m_GunSeasonKnown && m_PassGunSeason, gunsOn);
        }

        /// <summary>The per-run table, once per evaluated pass (right after BuildMask, before any early return). Per run: the measured share
        /// (or a natural run's carried-over measurement, or the day-mean estimate), the hysteretic label, the multiplier through the dead
        /// band. The generation bumps when any published State, Multiplier or Source changes or a run enters or leaves the table. Then the
        /// summary, the natural dress depth, SnowSettled's "every entry Measured", and the source / stale / open lines.</summary>
        private void RefreshRunSnow(uint frame, SnapshotChoice snap, float freezing, float margin, float now)
        {
            m_RunSnowNext.Clear();
            m_OpenChanges.Clear();
            bool changed = false;
            float dayMean = float.NaN;   // fetched lazily (SnowLineSystem caches it per game hour)
            float baseHeight = m_SnowLineSystem.GetBaseHeight();
            float lapse = m_SnowLineSystem.GetLapsePerM();
            int measured = 0, estimated = 0, open = 0, closed = 0, opening = 0, closing = 0;
            int full = 0, partly = 0, zero = 0;
            int uncalm = 0;   // measured runs with a share in [30 %, 70 %) (the share guard)
            // game hours since the last refresh (capped), for the protection budgets.
            float protectDtHours = m_ProtectFrameValid ? RunSnowRules.FramesToHours(frame - m_ProtectLastFrame) : 0f;
            m_ProtectLastFrame = frame;
            m_ProtectFrameValid = true;
            int protectActive = 0;
            double naturalDepthW = 0.0, naturalFootprintW = 0.0;
            for (int i = 0; i < m_RunSnowInputs.Count; i++)
            {
                RunSnowInput input = m_RunSnowInputs[i];
                bool hasPrev = m_RunSnow.TryGetValue(input.Run, out RunSnow prev);
                var next = new RunSnow
                {
                    Kind = input.Kind,
                    BottomY = input.BottomY,
                    TopY = input.TopY,
                    MeanDepthM = float.NaN,
                };
                float share = float.NaN;
                if (input.Measured)
                {
                    share = RunSnowRules.ShareFromSums(input.WhiteW, input.FootprintW);
                    if (!float.IsNaN(share))
                    {
                        next.Source = RunSnowSource.Measured;
                        next.MeanDepthM = (float)(input.DepthW / input.FootprintW * RunSnowRules.SnowHeightScaleM);
                        next.FootprintTexels = (float)input.FootprintW;
                        next.SnapshotFrame = snap.Frame;
                        if (input.Kind == RunKind.Natural || input.Kind == RunKind.CatTrack)
                            next.ExcludedShare = (float)(input.ExcludedW / (input.FootprintW + input.ExcludedW));
                    }
                }
                else if (input.CarryOver && hasPrev && prev.Source == RunSnowSource.Measured &&
                         frame - prev.SnapshotFrame <= RunSnowRules.MaxSnapshotAgeFrames)
                {
                    // A natural run whose measurement is not due this pass keeps its last one; so does any run while no snapshot is usable (a failed
                    // readback), within the age cap.
                    share = prev.Share;
                    next.Source = RunSnowSource.Measured;
                    next.MeanDepthM = prev.MeanDepthM;
                    next.FootprintTexels = prev.FootprintTexels;
                    next.ExcludedShare = prev.ExcludedShare;
                    next.SnapshotFrame = prev.SnapshotFrame;
                }
                if (float.IsNaN(share))
                {
                    if (float.IsNaN(dayMean))
                        dayMean = m_SnowLineSystem.GetDayMeanTemperature();
                    next.Source = RunSnowSource.Estimated;
                    // GetRunSnow's rule for a cat track too: out of the gun season this pass's guns raise nothing (GunGate), so a gun run
                    // holds natural snow only. A run's gun value is look only: gunsOn true.
                    share = RunSnowRules.EstimatedRunShare(input.Kind, input.BottomY, input.TopY, dayMean, freezing, margin, baseHeight, lapse,
                        m_GunSeasonKnown && m_PassGunSeason, input.GunsOn);
                }
                // A cat track: the larger of its snow (above: the snow map under it, or the estimate) and its made snow (the guns' model,
                // held by the runs' protection machine; RunSnowRules.StepTrackMadeSnow).
                if (input.Kind == RunKind.CatTrack)
                {
                    if (float.IsNaN(dayMean))
                        dayMean = m_SnowLineSystem.GetDayMeanTemperature();
                    share = StepTrack(input, share, dayMean, freezing, margin, baseHeight, lapse, protectDtHours);
                }
                // The share guard's input. Measured runs only: an estimated share comes from the day-mean temperature, not from the snapshot, so a
                // skip does not stale it.
                if (next.Source == RunSnowSource.Measured && !RunSnowRules.IsCalmShare(share))
                    uncalm++;
                next.Share = share;
                next.State = RunSnowRules.NextState(hasPrev ? prev.State : RunSnowState.Unknown, share);
                float m = RunSnowRules.OpenMultiplier(share);
                next.Multiplier = hasPrev && !RunSnowRules.Republish(prev.Multiplier, m) ? prev.Multiplier : m;
                if (!hasPrev || prev.State != next.State || prev.Multiplier != next.Multiplier ||
                    prev.Source != next.Source)
                    changed = true;
                if (hasPrev && prev.State != next.State)
                    m_OpenChanges.Add(new OpenChange { Run = input.Run, Old = prev.State, Now = next });
                m_RunSnowNext[input.Run] = next;
                // a gun run's protection machine steps on its freshly measured pack (natural and groomed runs have none).
                if (input.Kind == RunKind.Snowmaking || input.Kind == RunKind.Night)
                    StepRunProtection(input, next, protectDtHours, ref protectActive);

                if (next.Source == RunSnowSource.Measured)
                    measured++;
                else
                    estimated++;
                if (next.State == RunSnowState.Open)
                    open++;
                else
                    closed++;
                RunSnowPhase phase = RunSnowRules.Phase(next.State, next.Multiplier);
                if (phase == RunSnowPhase.Opening)
                    opening++;
                else if (phase == RunSnowPhase.Closing)
                    closing++;
                if (next.Multiplier >= 1f)
                    full++;
                else if (next.Multiplier > 0f)
                    partly++;
                else
                    zero++;
                if (input.Kind == RunKind.Natural && next.Source == RunSnowSource.Measured)
                {
                    naturalDepthW += next.MeanDepthM * (double)next.FootprintTexels;
                    naturalFootprintW += next.FootprintTexels;
                }
            }
            if (!changed)
            {
                if (m_RunSnowNext.Count != m_RunSnow.Count)
                {
                    changed = true;
                }
                else
                {
                    foreach (var pair in m_RunSnow)
                    {
                        if (!m_RunSnowNext.ContainsKey(pair.Key))
                        {
                            changed = true;
                            break;
                        }
                    }
                }
            }
            // Compared with what the estimate gave: the open labels of the table being replaced.
            bool firstMeasured = !m_FirstMeasuredLogged && measured > 0;
            int estOpen = 0;
            if (firstMeasured)
            {
                foreach (var pair in m_RunSnow)
                {
                    if (pair.Value.State == RunSnowState.Open)
                        estOpen++;
                }
            }

            Dictionary<Entity, RunSnow> swap = m_RunSnow;
            m_RunSnow = m_RunSnowNext;
            m_RunSnowNext = swap;
            PublishTracks();
            if (changed)
                m_RunSnowGeneration++;

            int runs = m_RunSnow.Count;
            m_Summary.Runs = runs;
            m_Summary.Measured = measured;
            m_Summary.Estimated = estimated;
            m_Summary.Open = open;
            m_Summary.Closed = closed;
            m_Summary.Opening = opening;
            m_Summary.Closing = closing;
            m_Summary.Source = measured > 0 ? RunSnowSource.Measured : RunSnowSource.Estimated;
            m_Summary.NewestSnapshotAgeFrames = NewestSnapshotAge(frame);
            m_MultFull = full;
            m_MultPartly = partly;
            m_MultZero = zero;
            // the natural runs' measured depth, for a city without a managed measurement.
            m_NaturalDressValid = naturalFootprintW >= RunSnowRules.MinFootprintTexels;
            m_NaturalDressDepthM = m_NaturalDressValid ? (float)(naturalDepthW / naturalFootprintW) : 0f;
            TryGetDressDepth(out m_Summary.DressDepthM, out m_Summary.DressSource);
            m_TableAllMeasured = measured == runs;   // an empty table: vacuously true
            m_UncalmRuns = uncalm;                   // read by the next pass's skip test
            m_ProtectActiveRuns = protectActive;     // an Active spell forbids a skip this pass

            if (firstMeasured)
            {
                m_FirstMeasuredLogged = true;
                if (float.IsNaN(dayMean))
                    dayMean = m_SnowLineSystem.GetDayMeanTemperature();
                Mod.Log.Info($"Snow [source] measured: runs now read the snow map (snapshot from sim frame {snap.Frame}, " +
                             $"{measured} of {runs} run(s) measured, {open} open); until now they were estimated from the " +
                             $"day-mean temperature ({dayMean:F1} C), which gave {estOpen} open");
            }
            // Once per usable -> stale transition. Only a snapshot that is really older than MaxSnapshotAgeFrames counts as stale: a snapshot
            // invalidated because a request is overwriting it, or by a failed readback (the top-up WARN says so), is not "old".
            if (snap.Usable)
            {
                m_LastSnapUsable = true;
                m_StaleLogged = false;
                m_LastUsableSnapFrame = snap.Frame;
            }
            else if (m_LastSnapUsable && !m_StaleLogged && runs > 0 &&
                     frame - m_LastUsableSnapFrame > RunSnowRules.MaxSnapshotAgeFrames)
            {
                m_StaleLogged = true;
                Mod.Log.Info($"Snow [source] estimated: the newest snow-map snapshot is {frame - m_LastUsableSnapFrame} " +
                             $"sim frames old (> {RunSnowRules.MaxSnapshotAgeFrames}) — runs use the day-mean temperature " +
                             "estimate until a new one arrives");
            }

            // Each label change, through m_OpenLog (slots: city open, closed, 1).
            for (int i = 0; i < m_OpenChanges.Count; i++)
            {
                OpenChange ch = m_OpenChanges[i];
                if (ch.Now.Source != RunSnowSource.Measured && float.IsNaN(dayMean))
                    dayMean = m_SnowLineSystem.GetDayMeanTemperature();
                string line = OpenLine(ch, frame, dayMean, open, runs);
                switch (m_OpenLog.Offer(now, false, line, open, closed, 1.0))
                {
                    case LogWindow.Verdict.Plain:
                        Mod.Log.Info("Snow [open] " + line);
                        break;
                    case LogWindow.Verdict.Summary:
                        WriteOpenSummary(now);
                        break;
                }
            }
            m_OpenChanges.Clear();
        }


        /// <summary>Snow made / held per run, read from this pass's merge (Upload against Readback over each run's texel box, the
        /// texels the run owns in the Owner plane). "Made" = raised toward a gun or groomed target; "held" = melted snow put
        /// back by protection. Runtime only, per load; main thread, after the merge.</summary>
        private void AccumulateLedger()
        {
            if (m_LedgerRuns.Count == 0)
                return;
            int seasonKey = RunSnowRules.SnowSeasonKey(m_TimeSystem.year, m_TimeSystem.normalizedDate, m_GunWinterStart);
            var partials = new List<NativeArray<float2>>(m_LedgerRuns.Count);
            JobHandle chain = default;
            try
            {
                for (int k = 0; k < m_LedgerRuns.Count; k++)
                {
                    LedgerRun lr = m_LedgerRuns[k];
                    var rows = new NativeArray<float2>(lr.Y1 - lr.Y0 + 1, Allocator.TempJob);
                    partials.Add(rows);
                    chain = new LedgerRowsJob
                    {
                        Readback = m_Readback,
                        Upload = m_Upload,
                        Mask = m_Mask,
                        Owner = m_Owner,
                        Partials = rows,
                        RunY0 = lr.Y0,
                        RunX0 = lr.X0,
                        RunX1 = lr.X1,
                        Slot = lr.Slot,
                    }.ScheduleParallel(rows.Length, RowBatch, chain);
                }
                chain.Complete();
                for (int k = 0; k < m_LedgerRuns.Count; k++)
                {
                    double made = 0.0, held = 0.0;
                    NativeArray<float2> rows = partials[k];
                    for (int r = 0; r < rows.Length; r++)
                    {
                        made += rows[r].x;
                        held += rows[r].y;
                    }
                    if (made <= 0.0 && held <= 0.0)
                        continue;
                    Entity run = m_LedgerRuns[k].Run;
                    m_Ledger.TryGetValue(run, out RunSnowRules.RunLedger ledger);
                    ledger.Add(seasonKey, RunSnowRules.VolumeM3(made, TexelAreaM2), RunSnowRules.VolumeM3(held, TexelAreaM2));
                    m_Ledger[run] = ledger;
                }
            }
            finally
            {
                chain.Complete();
                for (int k = 0; k < partials.Count; k++)
                    partials[k].Dispose();
            }
        }

        /// <summary>The run's snow ledger (cubic meters this season and in total; made by guns or grooming, and held by protection). False
        /// before the run has gained anything this load. Main thread. The catalog's dump row prints it.</summary>
        internal bool TryGetRunLedger(Entity run, out RunSnowRules.RunLedger ledger)
        {
            return m_Ledger.TryGetValue(run, out ledger);
        }

        /// <summary>The run's protection record (state, game hours used). False for a run that never had one.</summary>
        internal bool TryGetRunProtection(Entity run, out RunSnowRules.ProtectRun spell)
        {
            return m_Protect.TryGetValue(run, out spell);
        }

        /// <summary>Steps the protection machine of one gun run for this refresh (its pack just measured: <paramref name="next"/>) and
        /// writes the start / end INFO line. Called from RefreshRunSnow for snowmaking and night runs only.</summary>
        private void StepRunProtection(in RunSnowInput input, in RunSnow next, float dtHours, ref int active)
        {
            m_Protect.TryGetValue(input.Run, out RunSnowRules.ProtectRun spell);
            float meanY = 0.5f * (input.BottomY + input.TopY);
            float runC = m_SnowLineSystem.GetLocalTemperature(meanY);
            bool packOpen = next.Source == RunSnowSource.Measured && next.State == RunSnowState.Open;
            RunSnowRules.ProtectState before = spell.State;
            RunSnowRules.ProtectEnd end = RunSnowRules.StepProtection(ref spell, dtHours, input.Contained, input.Cold,
                packOpen, runC, m_PassSpring, out bool started);
            m_Ledger.TryGetValue(input.Run, out RunSnowRules.RunLedger ledger);
            if (started)
            {
                spell.HeldAtStartM3 = ledger.HeldTotalM3;
                Mod.Log.Info($"Snow [protect] run {input.Run.Index}:{input.Run.Version} ({KindName(input.Kind)}) protection " +
                             $"started: pack open ({next.Share * 100f:F0} % skiable, mean {next.MeanDepthM:F2} m), " +
                             $"{input.Contained - input.Cold} of {input.Contained} texel(s) too warm for the guns, air at the run " +
                             $"{runC:F1} C; budget {RunSnowRules.EffectiveBudgetHours(m_PassSpring, 1f):F0} game h on a flat texel " +
                             $"({RunSnowRules.EffectiveBudgetHours(m_PassSpring, RunSnowRules.ProtectSouthFactor):F0} south .. " +
                             $"{RunSnowRules.MaxBudgetHours(m_PassSpring):F0} north{(m_PassSpring ? ", halved: spring" : "")}), " +
                             $"stops above {RunSnowRules.ProtectMaxLocalC:F0} C");
            }
            else if (end != RunSnowRules.ProtectEnd.None)
            {
                Mod.Log.Info($"Snow [protect] run {input.Run.Index}:{input.Run.Version} ({KindName(input.Kind)}) protection " +
                             $"ended ({ProtectEndText(end)}): used {spell.HoursUsed:F1} game h of " +
                             $"{RunSnowRules.EffectiveBudgetHours(m_PassSpring, 1f):F0} h (flat texel), put back " +
                             $"{ledger.HeldTotalM3 - spell.HeldAtStartM3:F0} m3; pack {next.Share * 100f:F0} % skiable, " +
                             $"mean {next.MeanDepthM:F2} m, air at the run {runC:F1} C");
            }
            if (before != RunSnowRules.ProtectState.Idle || spell.State != RunSnowRules.ProtectState.Idle)
                m_Protect[input.Run] = spell;
            if (spell.State == RunSnowRules.ProtectState.Active)
                active++;
        }

        /// <summary>One cat track's made snow for this refresh (RunSnowRules.StepTrackMadeSnow): seeded from its saved SkiTrackSnow on its
        /// first refresh of a load, raised by its guns (the estimate at freezing + the margin while GunsMakeSnow), held by the protection
        /// machine at the largest run budget, melted over TrackMeltOutHours after it. Queues the saved value when it moved. Returns the
        /// track's share: the larger of <paramref name="snowShare"/> (measured or estimated) and the made share.</summary>
        private float StepTrack(in RunSnowInput input, float snowShare, float dayMean, float freezing, float margin, float baseHeight,
            float lapse, float dtHours)
        {
            bool hasSaved = EntityManager.TryGetComponent(input.Run, out SkiTrackSnow saved);
            if (!m_TrackMade.TryGetValue(input.Run, out RunSnowRules.TrackMadeSnow made) && hasSaved)
                made.Share = saved.Share;
            float gun = RunSnowRules.TrackGunShare(m_GunSeasonKnown && m_PassGunSeason, input.GunsOn, input.BottomY, input.TopY, dayMean,
                freezing, margin, baseHeight, lapse);
            float cold = RunSnowRules.TrackColdShare(input.BottomY, input.TopY, dayMean, freezing, baseHeight, lapse);
            float trackC = m_SnowLineSystem.GetLocalTemperature(0.5f * (input.BottomY + input.TopY));
            float before = made.Share;
            RunSnowRules.ProtectEnd end = RunSnowRules.StepTrackMadeSnow(ref made, dtHours, gun, cold, trackC, m_PassSpring, out bool started);
            if (started)
                Mod.Log.Info($"Snow [track] cat track {input.Run.Index}:{input.Run.Version} made snow held: {made.Share * 100f:F0} % made, " +
                             $"guns cover {gun * 100f:F0} %, cold keeps {cold * 100f:F0} %, air at the track {trackC:F1} C; budget " +
                             $"{RunSnowRules.MaxBudgetHours(m_PassSpring):F0} game h (the largest run budget{(m_PassSpring ? ", halved: spring" : "")}), " +
                             $"then it melts over {RunSnowRules.TrackMeltOutHours:F0} game h; none above {RunSnowRules.ProtectMaxLocalC:F0} C");
            else if (end != RunSnowRules.ProtectEnd.None)
                Mod.Log.Info($"Snow [track] cat track {input.Run.Index}:{input.Run.Version} hold ended ({ProtectEndText(end)}): used " +
                             $"{made.Spell.HoursUsed:F1} game h; made {made.Share * 100f:F0} %, guns cover {gun * 100f:F0} %, cold keeps " +
                             $"{cold * 100f:F0} %, air at the track {trackC:F1} C");
            if ((before > 0f) != (made.Share > 0f))
                Mod.Log.Info($"Snow [track] cat track {input.Run.Index}:{input.Run.Version} made snow {(made.Share > 0f ? "made" : "gone")}: " +
                             $"{before * 100f:F0} % -> {made.Share * 100f:F0} % (guns {(input.GunsOn ? "on" : "Off")}, season " +
                             $"{(m_GunSeasonKnown ? (m_PassGunSeason ? "IN" : "OUT") : "unknown")}, day mean {dayMean:F1} C)");
            m_TrackMadeNext[input.Run] = made;
            int permille = SkiTrackSnow.ToPermille(made.Share);
            if (!hasSaved || saved.m_MadePermille != permille)
                m_TrackSaves.Add(new KeyValuePair<Entity, int>(input.Run, permille));
            return RunSnowRules.TrackShare(snowShare, made.Share);
        }

        /// <summary>After a refresh: the stepped made snow replaces the old (a track no longer in the table drops out), and the moved values
        /// are written to SkiTrackSnow (added once per track: a structural change on the main thread, at most once in its life).</summary>
        private void PublishTracks()
        {
            var swapMade = m_TrackMade;
            m_TrackMade = m_TrackMadeNext;
            m_TrackMadeNext = swapMade;
            m_TrackMadeNext.Clear();
            for (int i = 0; i < m_TrackSaves.Count; i++)
            {
                Entity track = m_TrackSaves[i].Key;
                if (!EntityManager.Exists(track) || EntityManager.HasComponent<Deleted>(track))
                    continue;
                var value = new SkiTrackSnow(m_TrackSaves[i].Value);
                if (EntityManager.HasComponent<SkiTrackSnow>(track))
                    EntityManager.SetComponentData(track, value);
                else
                    EntityManager.AddComponentData(track, value);
            }
            m_TrackSaves.Clear();
        }

        private static string ProtectEndText(RunSnowRules.ProtectEnd end)
        {
            switch (end)
            {
                case RunSnowRules.ProtectEnd.GunsBack: return "every texel cold again: the guns are back";
                case RunSnowRules.ProtectEnd.BudgetSpent: return "budget spent";
                case RunSnowRules.ProtectEnd.TooWarm: return $"air at the run above {RunSnowRules.ProtectMaxLocalC:F0} C";
                case RunSnowRules.ProtectEnd.PackLost: return "the pack fell below the open line";
                default: return "?";
            }
        }

        /// <summary>One Snow [made] line per game day when the season totals moved: every run with snow made or held this season. The hub
        /// run tooltip has no text field for it, so the log and the catalog dump row carry it.</summary>
        private void WriteMadeLine()
        {
            if (m_Ledger.Count == 0)
                return;
            double total = 0.0;
            foreach (var pair in m_Ledger)
                total += pair.Value.MadeTotalM3 + pair.Value.HeldTotalM3;
            if (total <= m_MadeLoggedTotalM3 + 1.0)
                return;
            m_MadeLoggedTotalM3 = total;
            var sb = new System.Text.StringBuilder();
            foreach (var pair in m_Ledger)
            {
                RunSnowRules.RunLedger l = pair.Value;
                if (sb.Length > 0)
                    sb.Append("; ");
                sb.Append("run ").Append(pair.Key.Index).Append(':').Append(pair.Key.Version).Append(' ')
                  .Append(l.MadeSeasonM3.ToString("F0", System.Globalization.CultureInfo.InvariantCulture)).Append(" m3 made + ")
                  .Append(l.HeldSeasonM3.ToString("F0", System.Globalization.CultureInfo.InvariantCulture)).Append(" held this season, ")
                  .Append(l.MadeTotalM3.ToString("F0", System.Globalization.CultureInfo.InvariantCulture)).Append(" + ")
                  .Append(l.HeldTotalM3.ToString("F0", System.Globalization.CultureInfo.InvariantCulture)).Append(" total");
            }
            Mod.Log.Info($"Snow [made] {m_DayText}: {sb} (1 cm over one texel = {TexelAreaM2:F0} m3; since this load, not saved)");
        }

        /// <summary>The body of the open line: `run {Index}:{Version} ({kind}) {old} -> {new}: skiable ... % of ... texel(s), mean ...
        /// m{excluded}, {source}; city: {open}/{runs} open`. The excluded share is printed for a measured natural run (an estimated one has
        /// no raster).</summary>
        private static string OpenLine(OpenChange ch, uint frame, float dayMean, int open, int runs)
        {
            RunSnow s = ch.Now;
            string excluded = s.Kind == RunKind.Natural && s.Source == RunSnowSource.Measured
                ? $", {s.ExcludedShare * 100f:F0} % excluded (injected neighbors)" : "";
            string source = s.Source == RunSnowSource.Measured
                ? $"measured (snapshot {frame - s.SnapshotFrame} sim frames old)"
                : $"estimated (day-mean {dayMean:F1} C)";
            return $"run {ch.Run.Index}:{ch.Run.Version} ({KindName(s.Kind)}) {ch.Old} -> {s.State}: skiable " +
                   $"{s.Share * 100f:F0} % of {s.FootprintTexels:F1} texel(s), mean {s.MeanDepthM:F3} m{excluded}, " +
                   $"{source}; city: {open}/{runs} open";
        }

        private static string KindName(RunKind kind)
        {
            switch (kind)
            {
                case RunKind.Groomed: return "groomed";
                case RunKind.Snowmaking: return "snowmaking";
                case RunKind.Night: return "night";
                case RunKind.Natural: return "natural";
                case RunKind.CatTrack: return "cat track";   // measured only, like a natural run
                default: return "kind " + (byte)kind;
            }
        }

        /// <summary>The age of the newest valid snapshot at <paramref name="frame"/>; uint.MaxValue when none.</summary>
        private uint NewestSnapshotAge(uint frame)
        {
            uint best = uint.MaxValue;
            if (m_ReadbackValid)
                best = frame - m_ReadbackFrame;
            if (m_MeasureValid && !m_MeasurePending && frame - m_MeasureFrame < best)
                best = frame - m_MeasureFrame;
            return best;
        }

        /// <summary>The margin and the gating setting, checked once per evaluated pass. A gating flip bumps RunSnowGeneration (GetRunSnow
        /// applies the setting live).</summary>
        private void NoteSettingChanges(float margin)
        {
            if (margin != m_LastMarginC)
            {
                Mod.Log.Info($"Snow [margin] snowmaking margin {m_LastMarginC:F1} -> {margin:F1} C (Options: Snowmaking " +
                             $"margin) — snow guns and night runs now work where T_local <= freezing+{margin:F1} C");
                m_LastMarginC = margin;
            }
            bool gating = m_SnowLineSystem.IsSnowGatingEnabled();
            if (gating != m_LastGating)
            {
                m_LastGating = gating;
                m_RunSnowGeneration++;
                Mod.Log.Info($"Snow [gating] 'Snow & season gating' {(gating ? "on" : "off")}: " +
                             (gating ? "runs open by their measured snow" : "every run counts as open"));
            }
        }

        /// <summary>One skipped pass (dry or packed); the first of an episode writes the episode line. Also the Snow [day] and cost counts
        /// of skipped passes.</summary>
        private void NoteSkip(TopupSkip kind, MaskStats stats, float ambient, float now, double maskMs)
        {
            m_SkipsSinceLastReal++;
            m_LastSkipKind = kind;
            if (m_InSkip && kind != m_SkipKind)   // defensive: the kind can only change after a real pass (step 15)
                EndSkip($"the skip changed from {KindWord(m_SkipKind)} to {KindWord(kind)}", now, force: false);
            if (!m_InSkip)
            {
                m_InSkip = true;
                m_SkipKind = kind;
                m_SkipEpisodeCount = 0;
                m_SkipEpisodeStartAt = now;
                OfferEpisodeLine(m_SkipLog, "Snow [skip]", "skip", kind == TopupSkip.Dry
                    ? $"dry: no run texel is cold enough (ambient {ambient:F1} C) and the {stats.ContainedTexels} footprint " +
                      "texel(s) were bare at the last real pass — readback, merge and upload skipped; a real pass still " +
                      $"runs every {RunSnowRules.ForcedRealPassFrames} sim frames"
                    // The map mean is the last real pass's, which the next merge reads.
                    : $"packed: no run texel is cold enough (ambient {ambient:F1} C) and the snowpack holds managed melt " +
                      $"(map mean {m_LastGlobalMeanDepthM:F3} m > {RunSnowRules.PackGuardMeanDepthM:F2} m; runs " +
                      $"{m_LastReal.RunMeanDepthM:F3} m), so a real pass would raise and lower nothing — readback, merge " +
                      $"and upload skipped; a real pass still runs every {RunSnowRules.ForcedRealPassFrames} sim frames, " +
                      "so run snow, open states and the dress follow the snow map up to " +
                      $"{RunSnowRules.ForcedRealPassFrames} sim frames late", force: false, now);
            }
            m_SkipEpisodeCount++;
            if (kind == TopupSkip.Dry)
                m_DayDry++;
            else
                m_DayPacked++;
            m_DaySkipMs += maskMs;
            if (m_CostThisPass)
            {
                m_CostSkipped++;
                if (kind == TopupSkip.Packed)
                    m_CostSkippedPacked++;
                m_CostSkippedMask += maskMs;
            }
        }

        /// <summary>Ends an open skip episode (of either kind). The kind word is taken before m_SkipKind is cleared.</summary>
        private void EndSkip(string reason, float now, bool force)
        {
            if (!m_InSkip)
                return;
            m_InSkip = false;
            string kind = KindWord(m_SkipKind);
            m_SkipKind = TopupSkip.None;
            OfferEpisodeLine(m_SkipLog, "Snow [skip]", "skip",
                $"{kind} ended after {m_SkipEpisodeCount} skipped pass(es) over {now - m_SkipEpisodeStartAt:F0} s: {reason}",
                force, now);
        }

        private void EndSkipIfOpen(MaskStats stats, float now)
        {
            if (m_InSkip)
                EndSkip(SkipEndReason(stats, !m_LastGating || m_UncalmRuns == 0), now, force: false);
        }

        /// <summary>The first failing term of the skip predicate, in its order (the frames term is the forced pass, which does not end an
        /// episode by itself). A packed episode also ends when a measured share left the calm band (the share guard; numbers interpolated).</summary>
        private string SkipEndReason(MaskStats stats, bool calm)
        {
            if (m_ProtectActiveRuns > 0)
                return $"warm-spell protection is on for {m_ProtectActiveRuns} run(s): a real pass every window (T09)";
            if (stats.ColdTexels > 0)
                return $"a run texel is cold enough ({stats.ColdTexels} texel(s))";
            if (m_LastReal.ColdTexels > 0)
                return $"a run texel is cold enough ({m_LastReal.ColdTexels} texel(s))";
            if (stats.ContainedTexels == 0 || stats.ContainedTexels != m_LastReal.ContainedTexels ||
                stats.FootprintHash != m_LastReal.FootprintHash)
                return $"the footprint changed ({m_LastReal.ContainedTexels} -> {stats.ContainedTexels} texel(s))";
            if (m_SkipKind == TopupSkip.Packed && !calm)
                return $"a run's snow share is near the open line ({m_UncalmRuns} run(s) between " +
                       $"{RunSnowRules.CalmClosedShare * 100:F0} % and {RunSnowRules.CalmOpenShare * 100:F0} %)";
            return "the forced real pass found snow on the footprint";
        }

        /// <summary>Why a forced check ended its episode: the record it just made would skip the other way (dry and packed) or not at all.
        /// Packed to none cannot come from Lowered or Injected (the forced merge ran with the pack guard on and no cold texel) nor from the
        /// share guard (the same `calm` let the check be forced), so it is the pack gone or the runs under the dress guard. Every number is
        /// interpolated.</summary>
        private string ForcedEndReason(TopupSkip was, TopupSkip next)
        {
            if (was == TopupSkip.Dry)
                return next == TopupSkip.Packed
                    ? "the forced real pass found snow on the footprint; the snowpack holds managed melt (map mean " +
                      $"{m_LastGlobalMeanDepthM:F3} m) — a packed skip follows"
                    : "the forced real pass found snow on the footprint";
            if (next == TopupSkip.Dry)
                return "the forced real pass found the footprint bare — a dry skip follows";
            if (!m_LastReal.PackGuardNext)
                return $"the forced real pass found the snowpack gone (map mean {m_LastGlobalMeanDepthM:F3} m <= " +
                       $"{RunSnowRules.PackGuardMeanDepthM:F2} m) — managed melt resumes";
            return $"the forced real pass found the runs under {RunSnowRules.PackedSkipMinRunDepthM:F2} m (mean " +
                   $"{m_LastReal.RunMeanDepthM:F3} m) — real passes while props and decals follow the melt";
        }

        /// <summary>The skip's word in the episode lines and the top-up suffix.</summary>
        private static string KindWord(TopupSkip kind)
        {
            switch (kind)
            {
                case TopupSkip.Dry: return "dry";
                case TopupSkip.Packed: return "packed";
                default: return "kind " + (byte)kind;
            }
        }

        /// <summary>One Snow [day] line per game day, keyed by SkiDayRules.DayKey (the mod's one day key, the SkiGuestCounterSystem.AdvanceDay
        /// pattern). The key moves every kTicksPerDay = 262,144 ticks: normalizedDate is the tick count mod 262,144 x daysPerYear over that
        /// (TimeSystem.cs:18, :38, :120-124; identical in 1.6.2f1). The first pass of a load starts its day without a line and marks it "from
        /// the load". Main thread (Topup).</summary>
        private void AdvanceSnowDay()
        {
            int day = SkiDayRules.DayKey(m_TimeSystem.year, m_TimeSystem.normalizedDate, m_TimeSystem.daysPerYear);
            if (day == m_DayKey)
                return;
            bool first = m_DayKey == int.MinValue;
            if (!first)
            {
                WriteDayLine(null);
                WriteMadeLine();   // both belong to a day roll; the ledger is empty at the first pass of a load
            }
            m_DayKey = day;
            m_DayFromLoad = first;
            int month = SkiMoneyRules.MonthKey(m_TimeSystem.year, m_TimeSystem.normalizedDate);
            m_DayText = SkiMoneyRules.YearOfKey(month).ToString(System.Globalization.CultureInfo.InvariantCulture) + "-" +
                        SkiMoneyRules.MonthOfKey(month).ToString("D2", System.Globalization.CultureInfo.InvariantCulture) +
                        (m_TimeSystem.daysPerYear != 12 ? $" (day key {day})" : "");
            ZeroDayCounts();
        }

        /// <summary>The counted day's line, then the counters are zeroed. <paramref name="end"/> null = a day roll; "the load" / "quit" = a
        /// part day. "idle or failed" is every counted pass that was neither real nor skipped: the idle exits and the failed readbacks.
        /// Nothing is written when no pass was counted. Never reads the TimeSystem (also called from OnDestroy).</summary>
        private void WriteDayLine(string end)
        {
            // Silent until anything of ours was seen this load.
            if (m_DayPasses > 0 && ResortPresence.EverSeen)
            {
                string suffix = m_DayFromLoad
                    ? (end == null ? " (part day, from the load)" : $" (part day, from the load, to {end})")
                    : (end == null ? "" : $" (part day, to {end})");
                int skipped = m_DayDry + m_DayPacked;
                Mod.Log.Info($"Snow [day] {m_DayText}{suffix}: {m_DayPasses} top-up pass(es) — {m_DayReal} real " +
                             $"({m_DayForced} forced check(s) of a skip; mean {Mean(m_DayRealMs, m_DayReal):F2} ms), " +
                             $"{m_DayDry} dry-skipped + {m_DayPacked} packed-skipped (mean {Mean(m_DaySkipMs, skipped):F2} ms, " +
                             $"mask only), {m_DayPasses - m_DayReal - skipped} idle or failed (idle exits and failed readbacks)");
            }
            ZeroDayCounts();
        }

        private void ZeroDayCounts()
        {
            m_DayPasses = m_DayReal = m_DayForced = m_DayDry = m_DayPacked = 0;
            m_DayRealMs = m_DaySkipMs = 0.0;
        }

        /// <summary>Ends an open measure-only episode.</summary>
        private void EndMeasureOnly(string reason, float now, bool force)
        {
            if (!m_InMeasureOnly)
                return;
            m_InMeasureOnly = false;
            string latency = m_MeasureEpisodeReadbacks > 0
                ? $"{m_MeasureEpisodeLatMin}-{m_MeasureEpisodeLatMax}" : "n/a";
            OfferEpisodeLine(m_MeasureLog, "Snow [measure]", "measure-only",
                $"measure-only ended ({reason}) after {m_MeasureEpisodeReadbacks} readback(s), latency {latency} " +
                "rendered frame(s)", force, now);
        }

        /// <summary>The disable path (OnUpdate's catch, ValidateBuffer failure). The table is cleared (every run is then an on-the-spot
        /// estimate), the generation bumped, and a line written once. Never throws (it runs inside a catch).</summary>
        private void ClearRunSnow()
        {
            try
            {
                m_RunSnow.Clear();
                m_RunSnowNext.Clear();
                m_RunSnowInputs.Clear();
                m_OpenChanges.Clear();
                ClearGuns();                 // a disabled injection runs no guns, so no plume reads running
                m_RunSnowGeneration++;
                m_NaturalDressValid = false;
                m_TableAllMeasured = false;
                m_UncalmRuns = 0;            // no measured run, no share guard
                m_Summary = default;
                m_Summary.Source = RunSnowSource.Estimated;   // every run is now an on-the-spot estimate
                m_MultFull = m_MultPartly = m_MultZero = 0;
                if (!m_SourceDisabledLogged)
                {
                    m_SourceDisabledLogged = true;
                    Mod.Log.Info("Snow [source] estimated: snow injection is disabled for this load — every run uses " +
                                 "the day-mean temperature estimate");
                }
            }
            catch (Exception)
            {
                // the disable path must not throw; the table is per load and reset at the next one
            }
        }


        private void ResetCostRecord()
        {
            m_CostLogged = false;
            m_CostThisPass = false;
            m_CostPasses = 0;
            m_CostFirstAt = 0f;
            m_CostLastAt = 0f;
            m_CostReal = 0;
            m_CostRealMask = m_CostRealReadback = m_CostRealMerge = m_CostRealUpload = 0.0;
            m_CostSkipped = 0;
            m_CostSkippedPacked = 0;
            m_CostSkippedMask = 0.0;
            m_CostNaturalPasses = 0;
            m_CostNaturalRunsMax = 0;
            m_CostNaturalMs = 0.0;
            m_CostMeasureRequests = 0;
            m_CostMeasureRequestMs = 0.0;
            m_CostLatencyMin = int.MaxValue;
            m_CostLatencyMax = -1;
            m_CostMeasuredTexels = 0;
        }

        /// <summary>Every pass that built the mask counts as evaluated (real, skipped or idle). Also counts the passes where the x4 step cap
        /// binds. The cost record takes only passes with at least one bound run to measure (injected or natural): an empty city would
        /// otherwise write the line over 64 idle passes.</summary>
        private void NoteEvaluatedPass(float now, uint elapsedFrames, MaskStats stats)
        {
            if (elapsedFrames / (float)Tuning.TopupIntervalFrames > RunSnowRules.MaxStepScale)
                m_StepClampedPasses++;
            int boundInjected = stats.SnowmakingTotal + stats.GroomedTotal + stats.NightTotal - stats.UnboundRuns;
            bool work = boundInjected > 0 || stats.NaturalScheduled > 0;
            m_CostThisPass = work && !m_CostLogged && m_CostPasses < CostPasses;
            if (!m_CostThisPass)
                return;
            if (m_CostPasses == 0)
                m_CostFirstAt = now;
            m_CostLastAt = now;
            m_CostPasses++;
            m_CostMeasuredTexels += stats.MeasuredTexels;
            if (stats.NaturalMeasured > 0 || stats.NaturalMs > 0.0)
            {
                m_CostNaturalPasses++;
                m_CostNaturalMs += stats.NaturalMs;
                if (stats.NaturalMeasured > m_CostNaturalRunsMax)
                    m_CostNaturalRunsMax = stats.NaturalMeasured;
            }
        }

        private void NoteRealPass(double maskMs, double readbackMs, double mergeMs, double uploadMs)
        {
            if (!m_CostThisPass)
                return;
            m_CostReal++;
            m_CostRealMask += maskMs;
            m_CostRealReadback += readbackMs;
            m_CostRealMerge += mergeMs;
            m_CostRealUpload += uploadMs;
        }

        private static double Mean(double sum, int count)
        {
            return count > 0 ? sum / count : 0.0;
        }

        /// <summary>Once per load: after CostPasses evaluated passes (OnUpdate), or at OnGamePreload when 8-63 ran. The rate counts
        /// intervals ((N - 1) / span): the floor caps every interval at 0.5 s, so it never exceeds the cap; "with {nr} run(s)" is the most
        /// natural runs one pass measured.</summary>
        private void WriteCostLine()
        {
            m_CostLogged = true;
            int n = m_CostPasses;
            float span = m_CostLastAt - m_CostFirstAt;
            double rate = n > 1 && span > 0f ? (n - 1) / (double)span : 0.0;
            string latency = m_CostLatencyMax >= 0 ? $"{m_CostLatencyMin}-{m_CostLatencyMax}" : "n/a";
            Mod.Log.Info($"Snow [cost] first {n} evaluated passes with a bound run this load over {span:F0} s ({rate:F2} per wall-clock " +
                         $"second, cap {1f / RunSnowRules.MinPassIntervalS:F2}): {m_CostReal} real (mean ms: mask " +
                         $"{Mean(m_CostRealMask, m_CostReal):F2}, readback {Mean(m_CostRealReadback, m_CostReal):F2}, merge " +
                         $"{Mean(m_CostRealMerge, m_CostReal):F2}, upload {Mean(m_CostRealUpload, m_CostReal):F2}), " +
                         $"{m_CostSkipped} skipped ({m_CostSkipped - m_CostSkippedPacked} dry, {m_CostSkippedPacked} packed; " +
                         $"mean mask {Mean(m_CostSkippedMask, m_CostSkipped):F2} ms), natural " +
                         $"measurement {Mean(m_CostNaturalMs, m_CostNaturalPasses):F3} ms mean over {m_CostNaturalPasses} " +
                         $"pass(es) with {m_CostNaturalRunsMax} run(s), measure-only readbacks {m_CostMeasureRequests} " +
                         $"(request mean {Mean(m_CostMeasureRequestMs, m_CostMeasureRequests):F3} ms, latency {latency} " +
                         $"rendered frame(s)), per-run measurement over {(n > 0 ? m_CostMeasuredTexels / n : 0)} texel(s) " +
                         $"per pass, step clamped at x{RunSnowRules.MaxStepScale:F0} on {m_StepClampedPasses} pass(es)");
        }

        // Buffer contract guard — the single dependency surface. Checked once per load;
        // any drift means a game patch changed snow internals: WARN once and disable.
        // Failure mode is "no injected snow", never corruption (we write nothing).
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
                ClearRunSnow();   // no measurement can exist this load: every run is estimated
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

        /// <summary>Both snapshot arrays (session-scoped, uninitialized; validity is per load, m_ReadbackValid / m_MeasureValid). Called
        /// right after ValidateBuffer succeeds and before BuildMask, so the jobs always get created arrays (a job never reads one whose
        /// Measure flag is off).</summary>
        private void EnsureSnapshotBuffers()
        {
            if (!m_Readback.IsCreated)
                m_Readback = new NativeArray<Ushort2>(TexelCount, Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory);
            if (!m_MeasureReadback.IsCreated)
                m_MeasureReadback = new NativeArray<Ushort2>(TexelCount, Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory);
        }

        /// <summary>The upload staging array and GPU buffer.</summary>
        private void EnsureGpuBuffers()
        {
            if (!m_Upload.IsCreated)
                m_Upload = new NativeArray<float2>(TexelCount, Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory);
            if (m_UploadBuffer == null)
                m_UploadBuffer = new ComputeBuffer(TexelCount, UnsafeUtility.SizeOf<float2>(),
                    ComputeBufferType.Default); // stride mirrors SnowSystem.Deserialize (:321)
        }

        // Mask build: run polygons -> per-texel target depth (normalized /8 m) in snow-buffer texel space. Texel (x,y) covers world
        // playableOffset + ((x,y)+0.5)/1024 * playableArea (snow UV == playable UV, TerrainSystem.cs:2502-2508). Per run: triangles
        // accumulate additive supersampled coverage in a scratch plane (triangles of one polygon never overlap, so partial coverages on
        // shared texels sum correctly), then fold max(coverage x target) into the mask: max-blend across runs, so overlapping runs take the
        // deeper target.
        /// <summary>One scheduled run's pending results, reduced after the single
        /// Complete(): per-run Active attribution stays exactly what the sequential fold
        /// produced — a texel counts for whichever run claimed it first.</summary>
        private struct PendingRun
        {
            public int Kind; // 0 night, 1 snowmaking, 2 groomed — night-first precedence (3 natural, never reduced)
            public Entity Run; // the run entity, for the per-run active-texel map
            public NativeList<TriData> Tris;
            public NativeArray<FoldRowPartial> Partials;
            // The public kind and the heights for RefreshRunSnow; natural runs carry Natural = true and are skipped by the mask reduction
            // (never MaskTexels, DecayTexels, the active counts or m_RunTexels: TryGetRunActiveTexels stays false for them).
            public RunKind PublicKind;
            public bool Natural;
            public bool GunsOn;             // a cat track's gun switch (measure-only runs); true otherwise
            public float BottomY, TopY;
            public ushort Slot;             // 1..N in schedule order (the Owner plane value; 0 for a natural run)
            public int X0, Y0, X1, Y1;      // the run's texel bounds (the ledger job's box)
            public bool Protecting;         // this pass's fold held warm texels of the run
            public bool GunRun;             // a snowmaking or night run (its guns follow SnowGunPlumeRules)
            public SnowGunPlumeRules.GunState GunState;   // the state this pass's fold used
        }

        /// <summary>margin = the snowmaking margin, snap = the snapshot this pass measures from, frame = this pass's sim frame. Fills
        /// m_RunSnowInputs (one entry per scheduled run of any type) for RefreshRunSnow.</summary>
        private MaskStats BuildMask(float2 playOffset, float2 playSize, float ambient, float freezing,
            float margin, SnapshotChoice snap, uint frame)
        {
            if (!m_Mask.IsCreated)
            {
                m_Mask = new NativeArray<float>(TexelCount, Allocator.Persistent);
                m_DecayMask = new NativeArray<float>(TexelCount, Allocator.Persistent);
                m_RampMask = new NativeArray<float>(TexelCount, Allocator.Persistent);
                m_ProtectMask = new NativeArray<float>(TexelCount, Allocator.Persistent);
                m_ProtectBase = new NativeArray<float>(TexelCount, Allocator.Persistent);
                m_Owner = new NativeArray<ushort>(TexelCount, Allocator.Persistent);
                m_Scratch = new NativeArray<float>(TexelCount, Allocator.Persistent);
            }

            var stats = default(MaskStats);
            float2 texelSize = playSize / TexSize;
            TerrainHeightData heightData = m_TerrainSystem.GetHeightData();
            m_SnowLineSystem.GetLocalTemperatureCoefficients(out float lapseAmbient,
                out float lapseBaseHeight, out float lapseScale);

            // One chained job graph, one Complete(): clear planes, then per run rasterize -> fold -> scratch re-zero. Runs are chained
            // sequentially: the fold max-blends into shared planes and the scratch plane is reused, which also preserves the "first run claims
            // the texel" attribution rule. The main thread meanwhile only reads ECS buffers and builds TriData, which the jobs never touch.
            JobHandle chain = default;
            if (m_PlanesDirty)
            {
                chain = new ClearPlanesJob
                {
                    Mask = m_Mask,
                    DecayMask = m_DecayMask,
                    RampMask = m_RampMask,
                    ProtectMask = m_ProtectMask,
                    Owner = m_Owner,
                }.ScheduleParallel(TexSize, RowBatch, default);
                m_PlanesDirty = false;
            }

            // The snapshot every fold of this pass measures from (Measure = snap.Usable; with Measure false the jobs never read it).
            // m_RunSnowInputs gets one entry per scheduled run.
            NativeArray<Ushort2> snapshot = snap.FromMeasure ? m_MeasureReadback : m_Readback;
            m_RunSnowInputs.Clear();
            m_LedgerRuns.Clear();   // refilled by the reduction below, read by the ledger pass after the merge
            m_NaturalRuns.Clear();
            ulong naturalSig = 0;

            var pending = new List<PendingRun>();
            // The chunk reads below go through type handles, which do not wait for running jobs; GeometrySystem writes Triangle / Node /
            // Geometry at Modification2B.
            EntityManager.CompleteDependencyBeforeRO<Triangle>();
            EntityManager.CompleteDependencyBeforeRO<Node>();
            EntityManager.CompleteDependencyBeforeRO<Geometry>();
            EntityManager.CompleteDependencyBeforeRO<PrefabRef>();
            var chunks = m_RunScanQuery.ToArchetypeChunkArray(Allocator.Temp);
            try
            {
                var entityHandle = GetEntityTypeHandle();
                var prefabRefHandle = GetComponentTypeHandle<PrefabRef>(isReadOnly: true);
                var geometryHandle = GetComponentTypeHandle<Geometry>(isReadOnly: true);   // heights
                var triangleHandle = GetBufferTypeHandle<Triangle>(isReadOnly: true);
                var nodeHandle = GetBufferTypeHandle<Node>(isReadOnly: true);
                for (int c = 0; c < chunks.Length; c++)
                {
                    var entities = chunks[c].GetNativeArray(entityHandle);
                    var prefabRefs = chunks[c].GetNativeArray(ref prefabRefHandle);
                    var geometries = chunks[c].GetNativeArray(ref geometryHandle);
                    var triangleAccessor = chunks[c].GetBufferAccessor(ref triangleHandle);
                    var nodeAccessor = chunks[c].GetBufferAccessor(ref nodeHandle);
                    for (int i = 0; i < prefabRefs.Length; i++)
                    {
                        // Night first: if a night prefab ever also landed in the snowmaking set, classifying night first guarantees it can never be
                        // mis-billed at the snowmaking rate.
                        bool nightRun = m_NightPrefabs.Contains(prefabRefs[i].m_Prefab);
                        bool snowmaking = !nightRun &&
                            m_SnowmakingPrefabs.Contains(prefabRefs[i].m_Prefab);
                        bool groomed = !nightRun && !snowmaking &&
                            m_GroomedPrefabs.Contains(prefabRefs[i].m_Prefab);
                        if (!nightRun && !snowmaking && !groomed)
                        {
                            // Natural runs come last and are only recorded here (entity, Geometry heights, a signature term); they are pre-clipped and measured
                            // after the injected chain, and only when a measurement is due. Never scheduled into the fold: no mask, decay or ramp texel, no
                            // active texels, no bill.
                            // The cat track (SkiRunCatTrackA, RunKind.CatTrack) is in none of the four sets, so it lands on this `continue`: no injection, no
                            // measurement, no bill from a mask (SkiRunUpkeepSystem bills its plan area while CatTrackGunsCanRun). A 6-12 m ribbon cannot be measured
                            // on the 14 m texel grid; GetRunSnow answers it by the day-mean estimate (RunSnowRules.ThresholdC gives it freezing + the gun margin,
                            // or freezing alone while its guns are Off, DECISIONS 568).
                            // Do not add its name to a set here.
                            // Since the run-types round the cat track is measured here too, as a natural run is (the snow map under its ribbon:
                            // coverage-weighted over partial texels; MinFootprintTexels decides when it is too small), and its guns' snow is the
                            // made-snow model in RefreshRunSnow. It is still never injected.
                            bool track = m_TrackMeasurePrefabs.Contains(prefabRefs[i].m_Prefab);
                            if (!track && !m_NaturalPrefabs.Contains(prefabRefs[i].m_Prefab))
                                continue;
                            stats.NaturalTotal++;
                            if (track)
                                stats.TrackTotal++;
                            if (!IsRunBound(entities[i]))
                            {
                                stats.NaturalUnbound++;
                                continue;
                            }
                            Entity ne = entities[i];
                            Geometry ng = geometries[i];
                            stats.NaturalScheduled++;
                            if (track)
                                stats.TrackScheduled++;
                            // The skiable bar of a natural run (RunSnowRules.SkiableBarNorm; 0.91 m for every band today). The band comes from the catalog's row
                            // (an unrated run reads band -1). A cat track has the bar of every run.
                            float naturalBar = track
                                ? RunSnowRules.SkiableBarNorm(RunKind.CatTrack, -1)
                                : RunSnowRules.SkiableBarNorm(RunKind.Natural, NaturalBand(ne));
                            m_NaturalRuns.Add(new NaturalRun
                            {
                                Run = ne,
                                BottomY = ng.m_Bounds.min.y,
                                TopY = ng.m_Bounds.max.y,
                                BarNorm = naturalBar,
                                Kind = track ? RunKind.CatTrack : RunKind.Natural,
                                GunsOn = !track || m_RunProps.CatTrackGunsOn(ne),   // the measure-only read of the switch (check-run-props R2)
                            });
                            naturalSig = unchecked(naturalSig +
                                (RunSnowRules.Mix64(((ulong)(uint)ne.Index << 32) | (uint)ne.Version) ^
                                 RunSnowRules.Mix64(math.asuint(ng.m_SurfaceArea)) ^
                                 RunSnowRules.Mix64(math.asuint(naturalBar)) ^
                                 RunSnowRules.Mix64(track ? 0x7472616BUL : 0UL)));   // a rating that lands re-measures; a track is its own term
                            continue;
                        }

                        // Per-texel thermal line: gating lives in the fold. Each texel injects or decays by the temperature at its own elevation, so the
                        // snow line literally crosses a run: top white, bottom bare, boundary creeping with the season.
                        float targetM;
                        float thermalThresholdC;
                        int kind;
                        RunKind publicKind;
                        if (nightRun)
                        {
                            // Aliases of the snowmaking values: identical physics by design, separate counters for billing.
                            stats.NightTotal++;
                            targetM = Tuning.NightTargetDepthM;
                            thermalThresholdC = freezing + margin;
                            kind = 0;
                            publicKind = RunKind.Night;
                        }
                        else if (snowmaking)
                        {
                            stats.SnowmakingTotal++;
                            targetM = Tuning.SnowmakingTargetDepthM;
                            thermalThresholdC = freezing + margin;   // the margin
                            kind = 1;
                            publicKind = RunKind.Snowmaking;
                        }
                        else
                        {
                            stats.GroomedTotal++;
                            targetM = Tuning.GroomedTargetDepthM;
                            thermalThresholdC = freezing + Tuning.GroomedMarginC;
                            kind = 2;
                            publicKind = RunKind.Groomed;
                        }

                        // Tether enforcement: unbound runs exist in the Totals but never rasterize (no snow, no bill, no score).
                        if (!IsRunBound(entities[i]))
                        {
                            stats.UnboundRuns++;
                            continue;
                        }

                        Geometry geometry = geometries[i];
                        // an Active protection spell of this gun run (never groomed, never natural) holds its warm texels
                        // in this pass's fold; the spell state was stepped by the last RefreshRunSnow.
                        bool protecting = false;
                        float hoursUsed = 0f;
                        if ((publicKind == RunKind.Snowmaking || publicKind == RunKind.Night) &&
                            m_Protect.TryGetValue(entities[i], out RunSnowRules.ProtectRun spell) &&
                            spell.State == RunSnowRules.ProtectState.Active)
                        {
                            protecting = true;
                            hoursUsed = spell.HoursUsed;
                        }
                        // a gun run raises its cold texels only while its guns run (SnowGunPlumeRules; Unknown until the
                        // first measurement of the load = no raise). Groomed runs have no guns and keep the old routing.
                        bool gunRun = publicKind == RunKind.Snowmaking || publicKind == RunKind.Night;
                        SnowGunPlumeRules.GunState gunState = SnowGunPlumeRules.GunState.Unknown;
                        if (gunRun)
                            m_GunState.TryGetValue(entities[i], out gunState);
                        ScheduleRun(entities[i], triangleAccessor[i], nodeAccessor[i],
                            playOffset, texelSize, targetM / SnowHeightScaleM,
                            thermalThresholdC, heightData, lapseAmbient,
                            lapseBaseHeight, lapseScale, kind, ref chain, pending,
                            ref stats, publicKind, geometry.m_Bounds.min.y, geometry.m_Bounds.max.y,
                            snapshot, snap.Usable, protecting, hoursUsed, gunRun, gunState);
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
                m_PlanesDirty = true;   // a fold may have written some planes before the throw
                DrainPending(ref chain, pending);
                throw;
            }
            finally
            {
                chunks.Dispose();
            }
            if (pending.Count > 0)
                m_PlanesDirty = true;   // this pass's folds wrote the planes; the next BuildMask clears them

            // Vanilla contract for job-side height reads (AreaResourceSystem /
            // GeometrySystem / LaneSystem precedent): register the chain as a CPU-height
            // reader so terrain writers gate on it. Redundant today — the chain completes
            // inside this same OnUpdate — but it makes the safety proof structural
            // instead of incidental.
            m_TerrainSystem.AddCPUHeightReader(chain);
            chain.Complete();

            // Reduce per-run partials in schedule order, identical attribution to the sequential fold; the same per-run numbers fill the
            // run -> texel map, plus each run's own measurement (float per row, double per run) and the footprint count, cold count and hash
            // over all bound injected runs.
            m_RunTexelsNext.Clear();
            m_GunStateNext.Clear();
            m_GunRunningNext.Clear();
            for (int p = 0; p < pending.Count; p++)
            {
                PendingRun run = pending[p];
                int newTexels = 0, newDecay = 0;
                int contained = 0, cold = 0, held = 0, stopped = 0;
                ulong footprintHash = 0;
                double footprintW = 0.0, whiteW = 0.0, depthW = 0.0;
                double gunTargetW = 0.0, gunPackW = 0.0;
                for (int r = 0; r < run.Partials.Length; r++)
                {
                    FoldRowPartial part = run.Partials[r];
                    newTexels += part.NewMaskTexels;
                    newDecay += part.NewDecayTexels;
                    contained += part.Contained;
                    cold += part.Cold;
                    held += part.Protected;
                    stopped += part.Stopped;
                    footprintHash = unchecked(footprintHash + part.FootprintHash);
                    footprintW += part.FootprintW;
                    whiteW += part.WhiteW;
                    depthW += part.DepthW;
                    gunTargetW += part.GunTargetW;
                    gunPackW += part.GunPackW;
                }
                stats.StoppedTexels += stopped;
                if (run.GunRun)
                    StepGuns(run, cold, snap.Usable ? SnowGunPlumeRules.PackDepthM(gunTargetW, gunPackW) : float.NaN);
                stats.ContainedTexels += contained;
                stats.ColdTexels += cold;
                stats.FootprintHash = unchecked(stats.FootprintHash + footprintHash);
                if (snap.Usable)
                    stats.MeasuredTexels += contained;
                m_RunSnowInputs.Add(new RunSnowInput
                {
                    Run = run.Run,
                    Kind = run.PublicKind,
                    BottomY = run.BottomY,
                    TopY = run.TopY,
                    Measured = snap.Usable,
                    // With no usable snapshot this pass (a failed blocking readback invalidates m_Readback; an injected-only city keeps no measure-only
                    // copy), a run measured within MaxSnapshotAgeFrames keeps its last measurement, as a natural run does, instead of flipping to the
                    // day-mean estimate for one pass.
                    CarryOver = !snap.Usable,
                    WhiteW = whiteW,
                    FootprintW = footprintW,
                    DepthW = depthW,
                    Contained = contained,
                    Cold = cold,
                    ProtectedTexels = held,
                });
                m_RunTexelsNext[run.Run] = newTexels;
                m_LedgerRuns.Add(new LedgerRun
                {
                    Run = run.Run, Kind = run.PublicKind, Slot = run.Slot,
                    X0 = run.X0, Y0 = run.Y0, X1 = run.X1, Y1 = run.Y1,
                });
                stats.MaskTexels += newTexels;
                stats.DecayTexels += newDecay;
                stats.ProtectedTexels += held;
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
            PublishRunTexels();
            PublishGuns();

            // Natural runs, after the injected chain is complete (Mask and DecayMask now hold this pass's whole managed footprint, which the
            // natural measurement excludes).
            if (m_NaturalRuns.Count > 0)
                MeasureNaturalRuns(playOffset, texelSize, snap, naturalSig, ref stats);
            return stats;
        }

        /// <summary>The second chain: per recorded natural run, pre-clip (main thread), then RasterizeRowsJob -> MeasureRowsJob ->
        /// ClearScratchRowsJob, runs one after another, one Complete(), timed on its own (stats.NaturalMs). Runs only when a measurement is
        /// due: a usable snapshot that is new, or a changed natural set (signature) or injected footprint (hash); otherwise every natural run
        /// is a carry-over, so in a natural-only city 15 of 16 passes do no natural raster work. A run with no on-map coverage gets an
        /// estimate. Same exception contract as the injected chain: buffers registered before their first job, the chain threaded through,
        /// drained on a throw.</summary>
        private void MeasureNaturalRuns(float2 playOffset, float2 texelSize, SnapshotChoice snap, ulong naturalSig,
            ref MaskStats stats)
        {
            bool due = snap.Usable &&
                       (snap.Frame != m_LastNaturalSnapshotFrame || snap.FromMeasure != m_LastNaturalFromMeasure ||
                        naturalSig != m_LastNaturalSignature || stats.FootprintHash != m_LastNaturalInjectedHash);
            if (!due)
            {
                for (int n = 0; n < m_NaturalRuns.Count; n++)
                {
                    NaturalRun nr = m_NaturalRuns[n];
                    m_RunSnowInputs.Add(new RunSnowInput
                    {
                        Run = nr.Run,
                        Kind = nr.Kind,
                        GunsOn = nr.GunsOn,
                        BottomY = nr.BottomY,
                        TopY = nr.TopY,
                        CarryOver = true,
                    });
                }
                return;
            }

            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            NativeArray<Ushort2> snapshot = snap.FromMeasure ? m_MeasureReadback : m_Readback;
            List<PendingRun> pending = m_NaturalPending;   // reused; empty between passes
            pending.Clear();
            JobHandle chain = default;
            int naturalTriangles = 0;   // not part of stats.Triangles (the top-up line counts injected runs)
            try
            {
                for (int n = 0; n < m_NaturalRuns.Count; n++)
                {
                    NaturalRun nr = m_NaturalRuns[n];
                    DynamicBuffer<Triangle> triangles = EntityManager.GetBuffer<Triangle>(nr.Run, true);
                    DynamicBuffer<Node> nodes = EntityManager.GetBuffer<Node>(nr.Run, true);
                    NativeList<TriData> tris = PreClip(triangles, nodes, playOffset, texelSize,
                        ref naturalTriangles, out int runX0, out int runY0, out int runX1, out int runY1);
                    if (runX0 > runX1)
                    {
                        tris.Dispose();
                        // No on-map coverage: kept in the table as an estimate.
                        m_RunSnowInputs.Add(new RunSnowInput
                        {
                            Run = nr.Run,
                            Kind = nr.Kind,
                            GunsOn = nr.GunsOn,
                            BottomY = nr.BottomY,
                            TopY = nr.TopY,
                        });
                        continue;
                    }
                    int rowCount = runY1 - runY0 + 1;
                    var partials = new NativeArray<FoldRowPartial>(rowCount, Allocator.TempJob);
                    pending.Add(new PendingRun
                    {
                        Kind = 3,
                        Run = nr.Run,
                        Tris = tris,
                        Partials = partials,
                        PublicKind = nr.Kind,
                        GunsOn = nr.GunsOn,
                        Natural = true,
                        BottomY = nr.BottomY,
                        TopY = nr.TopY,
                    });

                    chain = new RasterizeRowsJob
                    {
                        Tris = tris.AsArray(),
                        Scratch = m_Scratch,
                        RunY0 = runY0,
                        PlayOffset = playOffset,
                        TexelSize = texelSize,
                    }.ScheduleParallel(rowCount, RowBatch, chain);

                    chain = new MeasureRowsJob
                    {
                        Scratch = m_Scratch,
                        Mask = m_Mask,
                        DecayMask = m_DecayMask,
                        ProtectMask = m_ProtectMask,
                        RampMask = m_RampMask,   // stopped gun texels are managed too
                        Snapshot = snapshot,
                        Partials = partials,
                        RunY0 = runY0,
                        RunX0 = runX0,
                        RunX1 = runX1,
                        Measure = snap.Usable,
                        BarNorm = nr.BarNorm,   // 15 cm, or 31 cm for a natural run rated black or double black
                    }.ScheduleParallel(rowCount, RowBatch, chain);

                    chain = new ClearScratchRowsJob
                    {
                        Scratch = m_Scratch,
                        RunY0 = runY0,
                        RunX0 = runX0,
                        RunX1 = runX1,
                    }.ScheduleParallel(rowCount, RowBatch, chain);
                }
            }
            catch
            {
                DrainPending(ref chain, pending);
                throw;
            }
            chain.Complete();

            // Reduce in schedule order (float per row, double per run). Natural runs never touch the mask
            // statistics, the active counts or m_RunTexels.
            for (int p = 0; p < pending.Count; p++)
            {
                PendingRun run = pending[p];
                int contained = 0;
                double footprintW = 0.0, whiteW = 0.0, depthW = 0.0, excludedW = 0.0;
                for (int r = 0; r < run.Partials.Length; r++)
                {
                    FoldRowPartial part = run.Partials[r];
                    contained += part.Contained;
                    footprintW += part.FootprintW;
                    whiteW += part.WhiteW;
                    depthW += part.DepthW;
                    excludedW += part.ExcludedW;
                }
                stats.MeasuredTexels += contained;
                stats.NaturalMeasured++;
                m_RunSnowInputs.Add(new RunSnowInput
                {
                    Run = run.Run,
                    Kind = run.PublicKind,
                    GunsOn = run.GunsOn,
                    BottomY = run.BottomY,
                    TopY = run.TopY,
                    Measured = true,
                    WhiteW = whiteW,
                    FootprintW = footprintW,
                    DepthW = depthW,
                    ExcludedW = excludedW,
                });
                run.Partials.Dispose();
                run.Tris.Dispose();
            }
            pending.Clear();
            m_LastNaturalSnapshotFrame = snap.Frame;
            m_LastNaturalFromMeasure = snap.FromMeasure;
            m_LastNaturalSignature = naturalSig;
            m_LastNaturalInjectedHash = stats.FootprintHash;
            stats.NaturalMs = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 /
                              System.Diagnostics.Stopwatch.Frequency;
        }

        /// <summary>A managed throw mid-schedule must not leave jobs in flight against arrays a later pass (or OnDestroy) will touch: drain
        /// the chain and free the per-run buffers. An interrupted run may have rasterized coverage whose scratch re-zero never got scheduled.
        /// m_Disabled is per load but m_Scratch is per session, so a dirty scratch plane would read as phantom polygon coverage after the
        /// next load: restore the scratch-is-zero invariant (the caller rethrows).</summary>
        private void DrainPending(ref JobHandle chain, List<PendingRun> pending)
        {
            chain.Complete();
            foreach (PendingRun run in pending)
            {
                run.Partials.Dispose();
                run.Tris.Dispose();
            }
            pending.Clear();
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
        }

        /// <summary>Swaps the freshly reduced run -> texel map in and bumps RunTexelGeneration only when its content differs from the
        /// previous pass (count, a key or any value). O(runs), no allocation once both dictionaries have grown.</summary>
        private void PublishRunTexels()
        {
            bool changed = m_RunTexelsNext.Count != m_RunTexels.Count;
            if (!changed)
            {
                foreach (var pair in m_RunTexelsNext)
                {
                    if (!m_RunTexels.TryGetValue(pair.Key, out int old) || old != pair.Value)
                    {
                        changed = true;
                        break;
                    }
                }
            }
            Dictionary<Entity, int> swap = m_RunTexels;
            m_RunTexels = m_RunTexelsNext;
            m_RunTexelsNext = swap;
            if (changed)
                m_RunTexelGeneration++;
        }

        /// <summary>One run polygon: pre-clip its triangles to texel space (main thread; jobs never touch ECS buffers), then append
        /// rasterize -> fold -> scratch re-zero to the chain. Additive coverage into the scratch plane, fold into the mask over the run's
        /// texel bounds, re-zero only the touched bbox (no full-plane clears: cost stays proportional to run area). Texels beyond the
        /// playable area are clipped: the snow map ends there, backdrop runs get nothing. The fold's semantic load (containment, thermal
        /// routing, edge noise, spine, fringe cap) lives in FoldRowsJob.</summary>
        private void ScheduleRun(Entity run, DynamicBuffer<Triangle> triangles, DynamicBuffer<Node> nodes,
            float2 playOffset, float2 texelSize, float targetNorm, float thermalThresholdC,
            TerrainHeightData heightData, float lapseAmbient, float lapseBaseHeight,
            float lapseScale, int kind, ref JobHandle chain, List<PendingRun> pending,
            ref MaskStats stats, RunKind publicKind, float bottomY, float topY,
            NativeArray<Ushort2> snapshot, bool measure, bool protecting, float hoursUsed,
            bool gunRun, SnowGunPlumeRules.GunState gunState)
        {
            NativeList<TriData> tris = PreClip(triangles, nodes, playOffset, texelSize, ref stats.Triangles,
                out int runX0, out int runY0, out int runX1, out int runY1);

            if (runX0 > runX1)
            {
                tris.Dispose();
                return; // no coverage (degenerate or fully off-map)
            }

            int rowCount = runY1 - runY0 + 1;
            var partials = new NativeArray<FoldRowPartial>(rowCount, Allocator.TempJob);
            // Register for cleanup before scheduling, and reassign the caller's chain (by ref) after every ScheduleParallel: whatever throws
            // mid-way, BuildMask's catch-drain can always complete every scheduled job and dispose every buffer.
            ushort slot = (ushort)Math.Min(pending.Count + 1, ushort.MaxValue);   // the Owner plane value
            pending.Add(new PendingRun
            {
                Kind = kind,
                Run = run,
                Tris = tris,
                Partials = partials,
                PublicKind = publicKind,
                BottomY = bottomY,
                TopY = topY,
                Slot = slot,
                X0 = runX0, Y0 = runY0, X1 = runX1, Y1 = runY1,
                Protecting = protecting,
                GunRun = gunRun,
                GunState = gunState,
            });

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
                Snapshot = snapshot,      // read only when Measure
                Measure = measure,
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
                BarNorm = RunSnowRules.SkiableNorm,   // injected runs open at 3 ft (0.91 m), the bar of every run
                Slot = slot,
                ProtectMask = m_ProtectMask,
                Owner = m_Owner,
                Protecting = protecting,
                Spring = m_PassSpring,
                HoursUsed = hoursUsed,
                NorthSign = m_PassNorthSign,
                GunGate = gunRun && !SnowGunPlumeRules.RaisesThisPass(gunState, m_PassGunSeason),   // the guns raise only in the gun season
            }.ScheduleParallel(rowCount, RowBatch, chain);

            chain = new ClearScratchRowsJob
            {
                Scratch = m_Scratch,
                RunY0 = runY0,
                RunX0 = runX0,
                RunX1 = runX1,
            }.ScheduleParallel(rowCount, RowBatch, chain);
        }

        /// <summary>One polygon's triangles pre-clipped to texel space (main thread; jobs never touch ECS buffers), with the run's texel
        /// bounds; runX0 > runX1 when nothing lies on the playable area. The first half of ScheduleRun, shared with the natural
        /// measurement. The caller owns (and disposes) the returned list.</summary>
        private static NativeList<TriData> PreClip(DynamicBuffer<Triangle> triangles, DynamicBuffer<Node> nodes,
            float2 playOffset, float2 texelSize, ref int triangleCount,
            out int runX0, out int runY0, out int runX1, out int runY1)
        {
            runX0 = int.MaxValue;
            runY0 = int.MaxValue;
            runX1 = int.MinValue;
            runY1 = int.MinValue;

            var tris = new NativeList<TriData>(math.max(triangles.Length, 1), Allocator.TempJob);
            for (int t = 0; t < triangles.Length; t++)
            {
                int3 indices = triangles[t].m_Indices;
                if (math.cmax(indices) >= nodes.Length)
                    continue; // defensive: stale triangle vs node buffer
                triangleCount++;
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
            return tris;
        }


        /// <summary>Zero a float plane (the per-texel protection baseline at a load).</summary>
        private static void ZeroPlane(NativeArray<float> plane)
        {
            for (int i = 0; i < plane.Length; i++)
                plane[i] = 0f;
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

        // Fast PrefabID path + name-scan fallback, tolerating missing prefabs (the system logs its inertness once per load and sleeps until
        // the run surfaces exist).
        private void ResolveRunPrefabs()
        {
            if (m_PrefabsResolved)
                return;
            m_PrefabsResolved = true;
            ResolvePrefabSet(SnowmakingSurfaceNames, m_SnowmakingPrefabs);
            ResolvePrefabSet(GroomedSurfaceNames, m_GroomedPrefabs);
            ResolvePrefabSet(NightSurfaceNames, m_NightPrefabs);
            ResolvePrefabSet(NaturalSurfaceNames, m_NaturalPrefabs);   // measured only
            ResolvePrefabSet(CatTrackMeasureNames, m_TrackMeasurePrefabs);   // measured only
        }

        /// <summary>A run only operates (injects, bills, scores) while bound to a live sign. SkiRunOwnershipSystem does the binding
        /// (SkiRunBinding to the nearest sign within 5 km at bind time) and owns the bound test: binding present and its sign live (exists,
        /// not Deleted, sign prefab); a paid orphan is unbound. This is the one enforcement point, so every downstream consumer (upkeep reads
        /// active texels, attractiveness reads cover) gates for free. Unbound runs stay summer-bare; border props still spawn deliberately:
        /// installed hardware without snow is the player-visible "no sign in range" signal. Called for live runs only (m_RunScanQuery
        /// excludes Temp), never a Temp.</summary>
        private bool IsRunBound(Entity run)
        {
            return m_Ownership.IsBound(run);
        }

        /// <summary>The slope band (0 flat .. 4 double black, SkiResortScore.Band*) of a natural run from the catalog's last rebuild; -1 while
        /// it is unrated or the catalog is absent (the run then needs the default 15 cm). The catalog is resolved lazily and may stay null:
        /// nothing here depends on it being there.</summary>
        private int NaturalBand(Entity run)
        {
            if (m_Catalog == null)
                m_Catalog = World.GetExistingSystemManaged<SkiRunCatalogSystem>();
            if (m_Catalog != null && m_Catalog.TryGetRow(run, out SkiRunCatalogSystem.RunRow row) &&
                row.RateState == SkiRunCatalogSystem.RateRated)
                return row.Band;
            return -1;
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
