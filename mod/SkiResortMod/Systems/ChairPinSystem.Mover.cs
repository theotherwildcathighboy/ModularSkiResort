// The per-frame chair move pass at release scale. Target: under 0.5 ms a frame on the main thread for 20 lifts / ~3,000
// chairs, and no cost that grows with the city.
//
// WHAT RUNS WHEN
//   * On a loop-cache rebuild (lift edits, load, the 4,096-frame watchdog): BuildLoopCache seeds every loop's pace
//     (LiftMoverMath.Seed) and marks the flat arrays dirty; the next move pass flattens the loops ONCE into persistent native
//     arrays (EnsureMoverArrays: segments, chairs with offset / grip top / loop, a box per loop). A hold resume (Rebase) marks
//     them dirty too (the offsets moved). O(chairs + segments), never per frame.
//   * Every moving pass (every rendered frame the render clock advanced; RunMovePass), MAIN THREAD, O(loops) + the tags:
//       1. last frame's job is complete (OnUpdate's first line, MoverCompleteJob) and its result counts become the "last pass
//          moved / in place / missing" numbers (a missing chair dirties the cache);
//       2. per loop: a HELD loop keeps the managed path (its frozen pose is written at the hold's entry or a rebuild only, a
//          handful of frames per session); a running loop gets its camera distance (box), its stride (LiftMoverMath.StrideFor)
//          and the pace decision (LiftMoverMath.Decide: write this pass? tag bounds?);
//       3. the bounds tags of this pass (the loops that reached their refresh travel, plus held loops' entry tags) are added in
//          ONE batched EntityManager.AddComponent<Updated>: a structural change, so it runs BEFORE the job is scheduled (a
//          structural change completes every running job);
//       4. MoveChairsJob is scheduled (IJobParallelFor, batch 64, Burst) over all rostered chairs; a chair of a loop not
//          written this pass returns at once. SumResultsJob (IJob, Burst) folds the per-chair results into four ints.
//   * WORKER THREADS: MoveChairsJob, per chair: alive (EntityStorageInfoLookup.Exists, no Deleted), s = (offset + phase) % L
//     (phase = speed × TimeS computed on the main thread, PhaseOf's product: bit for bit the managed s), LiftLoopSampler.Sample
//     (= LiftLoopCache.SampleSegments) and LiftLoopSampler.Pose (= MoveChair's pose and 1 mm idempotence), then a Transform
//     write through ComponentLookup<Transform> only when the pose changed.
//
// THREADING / ECS CORRECTNESS
//   * The Transform write: [NativeDisableParallelForRestriction] ComponentLookup<Transform>. Safe because every index writes
//     a DIFFERENT entity (a chair is rostered once: BuildLoopCache adds each chair to one loop) and nothing else in the job
//     reads another chair's Transform. The stress mode never schedules two writers of one entity concurrently: its K passes are
//     CHAINED jobs (each depends on the previous).
//   * Dependency: the lookup is a RW declaration of Transform by this system, and the job handle is assigned to Dependency,
//     so the Entities dependency manager makes every later reader / writer of Transform this frame wait for it (Modification5
//     Objects.SearchSystem via UpdateCollectSystem, PreCulling, ObjectInterpolateSystem.UpdateStaticAnimations' copy into
//     InterpolatedTransform). A later main-thread EntityManager read or write of Transform (TramShuttleSystem's cabin pass,
//     a structural change by a barrier) completes the job first (the main thread then waits for what is left of it).
//   * Timing: Modification4B. Vanilla's Modification4B systems and ModificationBarrier4B run after this system
//     (SystemOrder.cs:83,:91); our job is done by the time PreCulling / Rendering read the Transforms.
//   * Our own native arrays are read by the job and written only on the main thread AFTER MoverCompleteJob, never while the
//     job can run; the dev tab and the dump read the managed loop cache, never these arrays.
//   * The lookups are refreshed (Update(this)) right before the schedule, after this pass's structural change.
//   * Held loops, Temp-chair previews, the static flow and BuildLoopCache's roster stay managed (rare events).
//
// FAR-LOOP PACE (LiftMoverMath header item 2): a loop whose box is ≥ 500 m from the camera is written every k-th pass, k ≤ 8,
// such that a chair never steps more than 0.5 mrad on screen; its bounds refresh every 2 m × k of its own travel; it is written
// (and its bounds refreshed when ≥ 2 m stale) on the very pass it comes back near. Positions are a function of the clock, so
// a throttled loop is written at its EXACT current pose (no catch-up error). Camera = CameraUpdateSystem.position (the active
// viewer's position, CameraUpdateSystem.cs:164,:268), which updates before PreCulling (SystemOrder.cs:631): at
// Modification4B it is the previous frame's (one frame of lag, inside the 500 m margin). No viewer → every loop near (fail open).
// Distance only, no frustum test: a loop behind the camera but within 500 m runs at full rate (cheap and safe).
//
// DEV TAB (ChairPinSystem.Debug.cs "Mover scale" foldout): Burst job ⇄ managed path (A/B), pace on / off, stress ×1 / ×4 /
// ×16 (the move work repeated K times per pass: K − 1 extra writes at an earlier clock, then the real pass; the Updated tags
// are not multiplied), sync-measure (the job is completed inside the timed span so the [cost] numbers include it). The
// once-per-load "[cost] chair mover" line reports p50 / p95 / max main-thread ms over the first 600 moving passes.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using Game.Common;
using Game.Rendering;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Transform = Game.Objects.Transform;

namespace SkiResortMod.Systems
{
    public sealed partial class ChairPinSystem
    {
        // Settings (static: the dev tab flips them; per app run, like MovingChairs)
        private static bool s_MoverUseJob = true;
        private static LiftMoverMath.PaceSettings s_Pace = LiftMoverMath.PaceSettings.Default;
        private static int s_StressFactor = 1;
        private static bool s_SyncMeasure;
        /// <summary>Stress mode: the extra passes run this many seconds of clock apart (so each one really rewrites).</summary>
        private const double StressShiftS = 0.5;
        /// <summary>The loop box grows by this much around the rope (a carrier hangs ≤ ~6 m below it, plus its size).</summary>
        private const float LoopBoxMarginM = 8f;
        /// <summary>The [cost] window: moving passes sampled per load (~10 s at 60 fps).</summary>
        private const int CostRingSize = MoverCostPassesToLog;

        private struct JobLoop
        {
            public int SegFirst, SegCount;
            public double Length, Phase, Speed;
            public byte Active;   // 1 = write this loop's chairs this pass
        }

        private struct JobChair
        {
            public Entity Chair;
            public double Offset;
            public float GripTop;
            public int Loop;
        }

        private struct NativeSegments : IFlatSegments
        {
            public NativeArray<FlatSegment> Array;
            public FlatSegment Get(int index) => Array[index];
        }

        /// <summary>Header WORKER THREADS. One index = one rostered chair (distinct entities: see THREADING).</summary>
        [BurstCompile]
        private struct MoveChairsJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<JobChair> Chairs;
            [ReadOnly] public NativeArray<JobLoop> Loops;
            [ReadOnly] public NativeArray<FlatSegment> Segments;
            [ReadOnly] public ComponentLookup<Deleted> Deleted;
            [ReadOnly] public EntityStorageInfoLookup Storage;
            [NativeDisableParallelForRestriction] public ComponentLookup<Transform> Transforms;
            public NativeArray<byte> Results;
            public double ShiftS;   // stress mode only; 0.0 = the real pass (phase + speed × 0.0 == phase exactly)
            /// <summary>The Burst probe: [0] = 1 when Execute(0) ran as managed code (MarkManaged is [BurstDiscard]: Burst
            /// strips the call, so a Burst-compiled job leaves it 0). Written at index 0 only, by Execute(0).</summary>
            public NativeArray<int> ManagedProbe;

            [BurstDiscard]
            private static void MarkManaged(NativeArray<int> probe)
            {
                probe[0] = 1;
            }

            public void Execute(int index)
            {
                if (index == 0)
                    MarkManaged(ManagedProbe);
                JobChair chair = Chairs[index];
                if ((uint)chair.Loop >= (uint)Loops.Length)
                {
                    Results[index] = LiftLoopSampler.Missing;
                    return;
                }
                JobLoop loop = Loops[chair.Loop];
                if (loop.Active == 0 || loop.SegCount <= 0 || !(loop.Length > 0.0))
                {
                    Results[index] = LiftLoopSampler.Skipped;
                    return;
                }
                Entity e = chair.Chair;
                if (!Storage.Exists(e) || Deleted.HasComponent(e) || !Transforms.TryGetComponent(e, out Transform transform))
                {
                    Results[index] = LiftLoopSampler.Missing;
                    return;
                }
                double s = (chair.Offset + (loop.Phase + loop.Speed * ShiftS)) % loop.Length;
                var segments = new NativeSegments { Array = Segments };
                LiftLoopSampler.Sample(ref segments, loop.SegFirst, loop.SegCount, s, out float3 position, out float3 tangent);
                byte result = LiftLoopSampler.Pose(position, tangent, chair.GripTop, ref transform);
                if (result == LiftLoopSampler.Moved)
                    Transforms[e] = transform;
                Results[index] = result;
            }
        }

        /// <summary>Folds the per-chair results into Counts[0..3] (Skipped, Moved, InPlace, Missing).</summary>
        [BurstCompile]
        private struct SumResultsJob : IJob
        {
            [ReadOnly] public NativeArray<byte> Results;
            public NativeArray<int> Counts;

            public void Execute()
            {
                int c0 = 0, c1 = 0, c2 = 0, c3 = 0;
                for (int i = 0; i < Results.Length; i++)
                {
                    switch (Results[i])
                    {
                        case LiftLoopSampler.Moved: c1++; break;
                        case LiftLoopSampler.InPlace: c2++; break;
                        case LiftLoopSampler.Missing: c3++; break;
                        default: c0++; break;
                    }
                }
                Counts[0] = c0;
                Counts[1] = c1;
                Counts[2] = c2;
                Counts[3] = c3;
            }
        }

        private NativeList<FlatSegment> m_JobSegments;
        private NativeList<JobLoop> m_JobLoops;
        private NativeList<JobChair> m_JobChairs;
        private NativeList<byte> m_JobResults;
        private NativeArray<int> m_JobCounts;
        private ComponentLookup<Transform> m_TransformLookup;
        private ComponentLookup<Deleted> m_DeletedLookup;
        private EntityStorageInfoLookup m_StorageLookup;
        private JobHandle m_MoveHandle;
        private bool m_MoveScheduled;
        private bool m_MoverArraysDirty = true;
        private bool m_MoverAllocated;
        private NativeArray<int> m_ManagedProbe;   // MoveChairsJob.ManagedProbe
        private int m_BurstProbeState;             // 0 = not probed this load, 1 = probe in flight, 2 = reported
        private string m_DiagBurst = "not probed yet";
        private CameraUpdateSystem m_Camera;

        // last completed job's counts (consumed by the next move pass)
        private bool m_JobCountsFresh;
        private int m_JobMoved, m_JobInPlace, m_JobMissing;

        // [cost] window (per load) and dev-tab numbers
        private readonly float[] m_CostRing = new float[CostRingSize];
        private int m_CostCount, m_CostNext;
        private readonly List<float> m_CostScratch = new List<float>(CostRingSize);
        private long m_TagTotal;                    // bounds Updated tags since the window start
        private int m_TagPasses;                    // passes that tagged anything
        private float m_TagWindowStart = -1f;       // realtimeSinceStartup at the first sampled pass
        private long m_WrittenTotal;                // chairs scheduled / written, summed over sampled passes
        private long m_SampledPasses;               // moving passes with riders since the load
        private int m_PassNear, m_PassFar, m_PassWritten, m_PassTags, m_PassRiding;
        private string m_DiagMoverPath = "-";

        private void MoverOnCreate()
        {
            m_TransformLookup = GetComponentLookup<Transform>(isReadOnly: false);
            m_DeletedLookup = GetComponentLookup<Deleted>(isReadOnly: true);
            m_StorageLookup = GetEntityStorageInfoLookup();
            m_JobSegments = new NativeList<FlatSegment>(256, Allocator.Persistent);
            m_JobLoops = new NativeList<JobLoop>(16, Allocator.Persistent);
            m_JobChairs = new NativeList<JobChair>(512, Allocator.Persistent);
            m_JobResults = new NativeList<byte>(512, Allocator.Persistent);
            m_JobCounts = new NativeArray<int>(4, Allocator.Persistent);
            m_ManagedProbe = new NativeArray<int>(1, Allocator.Persistent);
            m_MoverAllocated = true;
            m_Camera = World.GetOrCreateSystemManaged<CameraUpdateSystem>();
        }

        /// <summary>Completes the mover job if one is in flight and takes its counts. Never throws.</summary>
        private void MoverCompleteJob()
        {
            if (!m_MoveScheduled)
                return;
            m_MoveScheduled = false;
            try
            {
                m_MoveHandle.Complete();
                m_JobMoved = m_JobCounts[1];
                m_JobInPlace = m_JobCounts[2];
                m_JobMissing = m_JobCounts[3];
                m_JobCountsFresh = true;
                if (m_BurstProbeState == 1)
                {
                    // "Silence must be impossible": say once per load whether the mover really runs Burst-compiled.
                    m_BurstProbeState = 2;
                    bool managed = m_ManagedProbe[0] != 0;
                    m_DiagBurst = managed ? "NO — managed fallback" : "yes";
                    if (managed)
                        Mod.Log.Warn($"{nameof(ChairPinSystem)} [mover]: the chair move job runs WITHOUT Burst (managed fallback: " +
                                     "the mod's Burst library is missing or did not compile the job) — correct, but slower; " +
                                     "please report this line");
                    else
                        Mod.Log.Info($"{nameof(ChairPinSystem)} [mover]: the chair move job runs Burst-compiled on worker threads " +
                                     $"({m_JobChairs.Length} chair(s) per pass; far-loop pace {(s_Pace.Enabled ? "on" : "off")})");
                }
            }
            catch (Exception e)
            {
                Mod.Log.Warn($"{nameof(ChairPinSystem)} [mover]: completing the move job failed ({e.GetType().Name}: {e.Message})");
            }
        }

        /// <summary>Per Game / Editor load (OnGameLoadingComplete).</summary>
        private void MoverReset()
        {
            MoverCompleteJob();
            m_MoverArraysDirty = true;
            m_JobCountsFresh = false;
            m_JobMoved = m_JobInPlace = m_JobMissing = 0;
            m_CostCount = m_CostNext = 0;
            m_TagTotal = 0;
            m_TagPasses = 0;
            m_TagWindowStart = -1f;
            m_WrittenTotal = 0;
            m_SampledPasses = 0;
            m_PassNear = m_PassFar = m_PassWritten = m_PassTags = m_PassRiding = 0;
            m_DiagMoverPath = "-";
            m_BurstProbeState = 0;
            m_DiagBurst = "not probed yet";
            HarvestReset();
        }

        private void MoverDispose()
        {
            try
            {
                m_MoveHandle.Complete();
                m_MoveScheduled = false;
                if (m_MoverAllocated)
                {
                    m_MoverAllocated = false;
                    m_JobSegments.Dispose();
                    m_JobLoops.Dispose();
                    m_JobChairs.Dispose();
                    m_JobResults.Dispose();
                    m_JobCounts.Dispose();
                    m_ManagedProbe.Dispose();
                }
            }
            catch (Exception)
            {
                // teardown never throws
            }
        }

        /// <summary>The flat copy of the loop cache (main thread, job complete).</summary>
        private void EnsureMoverArrays()
        {
            if (!m_MoverArraysDirty)
                return;
            m_MoverArraysDirty = false;
            m_JobSegments.Clear();
            m_JobLoops.Clear();
            m_JobChairs.Clear();
            for (int l = 0; l < m_LoopCache.m_Loops.Count; l++)
            {
                LiftLoopCache.Loop loop = m_LoopCache.m_Loops[l];
                int first = m_JobSegments.Length;
                float3 min = new float3(float.MaxValue), max = new float3(float.MinValue);
                for (int s = 0; s < loop.m_Segments.Count; s++)
                {
                    FlatSegment flat = LiftLoopSampler.Flatten(loop.m_Segments[s]);
                    LiftLoopSampler.GrowBox(flat, ref min, ref max);
                    m_JobSegments.Add(flat);
                }
                loop.m_BoxKnown = loop.m_Segments.Count > 0 && math.all(math.isfinite(min)) && math.all(math.isfinite(max)) &&
                                  math.all(min <= max);
                loop.m_BoxMin = min - LoopBoxMarginM;
                loop.m_BoxMax = max + LoopBoxMarginM;
                m_JobLoops.Add(new JobLoop
                {
                    SegFirst = first,
                    SegCount = loop.m_Segments.Count,
                    Length = loop.m_Length,
                    Speed = loop.m_SpeedMps,
                });
                int n = math.min(loop.m_Chairs.Count, math.min(loop.m_Offsets.Count, loop.m_GripTops.Count));
                for (int i = 0; i < n; i++)
                {
                    m_JobChairs.Add(new JobChair
                    {
                        Chair = loop.m_Chairs[i],
                        Offset = loop.m_Offsets[i],
                        GripTop = loop.m_GripTops[i],
                        Loop = l,
                    });
                }
            }
            m_JobResults.ResizeUninitialized(m_JobChairs.Length);
        }

        /// <summary>One moving pass (header WHAT RUNS WHEN). Called by UpdateMovingChairs after the pause test, with this
        /// pass's clock, its advance since the last pass and whether the cache was rebuilt this frame.</summary>
        private void RunMovePass(double timeS, double passDtS, bool rebuilt)
        {
            long moverStart = Stopwatch.GetTimestamp();
            EnsureMoverArrays();
            bool useJob = s_MoverUseJob;
            int stress = math.max(1, s_StressFactor);
            LiftMoverMath.PaceSettings pace = s_Pace;
            bool haveCamera = m_Camera != null && m_Camera.TryGetViewer(out _);
            float3 camera = haveCamera ? m_Camera.position : float3.zero;

            int moved = 0, inPlace = 0, missing = 0, riding = 0, heldWrites = 0;
            int near = 0, far = 0, written = 0, tags = 0;
            bool anyActive = false;
            long boundsTicks = 0;

            // 1. last frame's job (completed in OnUpdate): its counts are this pass's "last pass" numbers in job mode.
            if (m_JobCountsFresh)
            {
                m_JobCountsFresh = false;
                if (m_JobMissing > 0)
                    m_LoopCacheDirty = true;   // a roster chair died: heal at the next rebuild (as MoveChair's Missing did)
            }

            var tagChairs = new NativeList<Entity>(64, Allocator.Temp);
            try
            {
                for (int l = 0; l < m_LoopCache.m_Loops.Count; l++)
                {
                    LiftLoopCache.Loop loop = m_LoopCache.m_Loops[l];
                    riding += loop.m_Chairs.Count;
                    JobLoop jl = l < m_JobLoops.Length ? m_JobLoops[l] : default;
                    jl.Active = 0;

                    // 2a. A HELD loop is written only at a rebuild or the hold's entry, at the frozen pose
                    // (LiftLoopHold.FrozenPhase = PhaseOf's product at FrozenTimeS, bit for bit), managed; its entry tag below.
                    if (m_HeldLoopCount > 0 && l < m_HoldByLoop.Count && m_HoldByLoop[l].Held)
                    {
                        LoopHold hold = m_HoldByLoop[l];
                        if (rebuilt || hold.PoseDirty)
                        {
                            double frozen = LiftLoopHold.FrozenPhase(loop.m_SpeedMps, hold.FrozenTimeS);
                            for (int i = 0; i < loop.m_Chairs.Count; i++)
                            {
                                double s = (loop.m_Offsets[i] + frozen) % loop.m_Length;
                                switch (MoveChair(loop.m_Chairs[i], loop.m_GripTops[i], loop, s))
                                {
                                    case MoveResult.Moved: heldWrites++; break;
                                    case MoveResult.InPlace: break;
                                    case MoveResult.Missing: missing++; break;
                                }
                            }
                            hold.PoseDirty = false;
                        }
                        // The hold-entry bounds tag, once, so CullingInfo and the static
                        // search tree hold the exact frozen pose (PreCullingSystem.cs:709-718 re-reads Transform on Updated only).
                        if (hold.TagBounds)
                        {
                            hold.TagBounds = false;
                            long c0 = Stopwatch.GetTimestamp();
                            int before = tagChairs.Length;
                            CollectChairsToTag(loop, tagChairs);
                            tags += tagChairs.Length - before;
                            boundsTicks += Stopwatch.GetTimestamp() - c0;
                        }
                        if (l < m_JobLoops.Length)
                            m_JobLoops[l] = jl;
                        continue;
                    }
                    if (loop.m_Chairs.Count == 0 || !(loop.m_Length > 0.0))
                    {
                        if (l < m_JobLoops.Length)
                            m_JobLoops[l] = jl;
                        continue;
                    }

                    // 2b. the pace (LiftMoverMath header item 2).
                    float distance = haveCamera && loop.m_BoxKnown
                        ? LiftMoverMath.BoxDistance(camera, loop.m_BoxMin, loop.m_BoxMax)
                        : 0f;
                    int stride = LiftMoverMath.StrideFor(distance, loop.m_SpeedMps, passDtS, pace);
                    bool write, tag;
                    if (rebuilt)
                    {
                        // A rebuild writes every running loop (the new offsets) and has tagged every rider Updated itself.
                        write = true;
                        tag = false;
                        loop.m_Pace.Stride = stride;
                    }
                    else
                    {
                        LiftMoverMath.Decide(ref loop.m_Pace, stride, timeS, loop.m_SpeedMps, out write, out tag);
                    }
                    if (stride > 1) far++;
                    else near++;
                    if (!write)
                    {
                        if (l < m_JobLoops.Length)
                            m_JobLoops[l] = jl;
                        continue;
                    }
                    written += loop.m_Chairs.Count;
                    if (tag)
                    {
                        long c0 = Stopwatch.GetTimestamp();
                        int before = tagChairs.Length;
                        CollectChairsToTag(loop, tagChairs);
                        tags += tagChairs.Length - before;
                        boundsTicks += Stopwatch.GetTimestamp() - c0;
                        m_LastBoundsTimeS = timeS;   // dump only (the cadence is per loop now)
                    }

                    double phase = PhaseOf(loop, timeS);
                    if (useJob)
                    {
                        jl.Active = 1;
                        jl.Phase = phase;
                        jl.Speed = loop.m_SpeedMps;
                        anyActive = true;
                    }
                    else
                    {
                        // The managed path (MoveChair, EntityManager per chair): the dev tab's A/B baseline.
                        for (int k = 0; k < stress; k++)
                        {
                            double shift = (k - (stress - 1)) * StressShiftS;   // the last pass: 0.0, the real clock
                            bool real = k == stress - 1;
                            for (int i = 0; i < loop.m_Chairs.Count; i++)
                            {
                                double s = real
                                    ? (loop.m_Offsets[i] + phase) % loop.m_Length
                                    : (loop.m_Offsets[i] + (phase + loop.m_SpeedMps * shift)) % loop.m_Length;
                                MoveResult r = MoveChair(loop.m_Chairs[i], loop.m_GripTops[i], loop, s);
                                if (!real)
                                    continue;
                                switch (r)
                                {
                                    case MoveResult.Moved: moved++; break;
                                    case MoveResult.InPlace: inPlace++; break;
                                    case MoveResult.Missing: missing++; break;
                                }
                            }
                        }
                    }
                    if (l < m_JobLoops.Length)
                        m_JobLoops[l] = jl;
                }

                // 3. the bounds tags: ONE structural change, BEFORE the job (header THREADING).
                if (tagChairs.Length > 0)
                {
                    long b0 = Stopwatch.GetTimestamp();
                    EntityManager.AddComponent<Updated>(tagChairs.AsArray());
                    boundsTicks += Stopwatch.GetTimestamp() - b0;
                }
            }
            finally
            {
                tagChairs.Dispose();
            }

            // 4. the job.
            if (useJob)
            {
                if (anyActive && m_JobChairs.Length > 0)
                {
                    m_TransformLookup.Update(this);
                    m_DeletedLookup.Update(this);
                    m_StorageLookup.Update(this);
                    JobHandle handle = Dependency;
                    if (m_BurstProbeState == 0)
                    {
                        m_ManagedProbe[0] = 0;   // the job is complete (OnUpdate's first line): main-thread write is safe
                        m_BurstProbeState = 1;
                    }
                    for (int k = 0; k < stress; k++)
                    {
                        var job = new MoveChairsJob
                        {
                            Chairs = m_JobChairs.AsArray(),
                            Loops = m_JobLoops.AsArray(),
                            Segments = m_JobSegments.AsArray(),
                            Deleted = m_DeletedLookup,
                            Storage = m_StorageLookup,
                            Transforms = m_TransformLookup,
                            Results = m_JobResults.AsArray(),
                            ShiftS = (k - (stress - 1)) * StressShiftS,
                            ManagedProbe = m_ManagedProbe,
                        };
                        handle = job.Schedule(m_JobChairs.Length, 64, handle);
                    }
                    handle = new SumResultsJob { Results = m_JobResults.AsArray(), Counts = m_JobCounts }.Schedule(handle);
                    Dependency = handle;
                    m_MoveHandle = handle;
                    m_MoveScheduled = true;
                    if (s_SyncMeasure)
                        MoverCompleteJob();   // measured inside the span below; the counts are this pass's
                }
                else
                {
                    m_JobMoved = m_JobInPlace = m_JobMissing = 0;   // nothing written this pass (every loop far and not due)
                }
                // The counts of the last COMPLETED job (one pass old unless sync-measure is on).
                moved = m_JobMoved;
                inPlace = m_JobInPlace;
                missing += m_JobMissing;
                if (s_SyncMeasure && m_JobCountsFresh)
                {
                    m_JobCountsFresh = false;
                    if (m_JobMissing > 0)
                        m_LoopCacheDirty = true;
                }
            }
            m_DiagMoverPath = useJob ? (s_SyncMeasure ? "Burst job (sync-measure)" : "Burst job") : "managed";

            long moverTicks = Stopwatch.GetTimestamp() - moverStart - boundsTicks;
            if (!useJob && missing > 0)
                m_LoopCacheDirty = true; // roster chair died mid-window — heal next rebuild

            // Diagnostics (plain fields; the dev tab formats them on read).
            m_PassNear = near;
            m_PassFar = far;
            m_PassWritten = written;
            m_PassTags = tags;
            m_PassRiding = riding;
            m_DiagLastMoved = moved;
            m_DiagLastInPlace = inPlace;
            m_DiagLastMissing = missing;
            m_DiagLastRiding = riding;
            if (moved > 0)
                m_DiagPassesWithMoves++;
            m_DiagHeldWrites += heldWrites;
            m_DiagLastExit = null;  // LastExitText() formats it from m_DiagLast* when the dev tab / dump reads it

            // A frozen-pose write proves the pass alive too. Job mode: "moved" is the last completed
            // job's count (one pass late), so the line still means chairs really moved.
            if ((moved > 0 || heldWrites > 0) && !m_MovingAliveLogged)
            {
                m_MovingAliveLogged = true;
                Mod.Log.Info($"{nameof(ChairPinSystem)} [moving] pass alive — {riding} chair(s) riding " +
                             $"{m_LoopCache.m_Loops.Count} loop(s), {m_StaticFallbackChairs} static-fallback" +
                             LiftLoopHold.HeldSuffix(m_HeldChairsForPower, m_HeldChairCount - m_HeldChairsForPower) +
                             $" (mover: {m_DiagMoverPath})");
            }

            // [cost] window: passes with riders only.
            if (riding > 0)
            {
                double msPerTick = 1000.0 / Stopwatch.Frequency;
                if (m_TagWindowStart < 0f)
                    m_TagWindowStart = UnityEngine.Time.realtimeSinceStartup;
                m_CostRing[m_CostNext] = (float)(moverTicks * msPerTick);
                m_CostNext = (m_CostNext + 1) % CostRingSize;
                if (m_CostCount < CostRingSize)
                    m_CostCount++;
                m_TagTotal += tags;
                m_WrittenTotal += written;
                m_SampledPasses++;
                if (tags > 0)
                {
                    m_TagPasses++;
                    m_BoundsCostTicks += boundsTicks;
                    m_BoundsCostPasses++;
                }
                if (!m_BoundsCostLogged)
                {
                    m_MoverCostTicks += moverTicks;
                    m_MoverCostPasses++;
                    if (m_MoverCostPasses >= MoverCostPassesToLog)
                    {
                        m_BoundsCostLogged = true;
                        Mod.Log.Info(MoverCostLine($"once per load, first {m_MoverCostPasses} moving passes"));
                    }
                }
            }
        }

        /// <summary>Dev tab: restart the [cost] window (ring, tag rate, written / pass, bounds mean) to measure one camera
        /// position or setting on its own.</summary>
        private void ResetMoverCostWindow()
        {
            m_CostCount = m_CostNext = 0;
            m_TagTotal = 0;
            m_TagPasses = 0;
            m_TagWindowStart = -1f;
            m_WrittenTotal = 0;
            m_SampledPasses = 0;
            m_BoundsCostTicks = 0;
            m_BoundsCostPasses = 0;
        }

        /// <summary>Dev tab rows: p50 / p95 / max of the current window (a sort of ≤ 600 floats into a reused list).</summary>
        private string MoverCostShort()
        {
            LiftMoverMath.Percentiles(m_CostRing, m_CostCount, m_CostScratch, out float p50, out float p95, out float max);
            return string.Format(CultureInfo.InvariantCulture, "{0:F3} / {1:F3} / {2:F3} ms ({3} passes)", p50, p95, max, m_CostCount);
        }

        /// <summary>The "[cost] chair mover" line: p50 / p95 / max main-thread ms over the sampled passes (the ring: the first
        /// 600 of the load, or the last 600 when called from the dev tab), chairs, loops near / far, chairs written per pass,
        /// bounds Updated tags per second (and the batched AddComponent's mean ms), the path and the pace settings.</summary>
        private string MoverCostLine(string window)
        {
            LiftMoverMath.Percentiles(m_CostRing, m_CostCount, m_CostScratch, out float p50, out float p95, out float max);
            double msPerTick = 1000.0 / Stopwatch.Frequency;
            double mean = 0.0;
            for (int i = 0; i < m_CostCount; i++)
                mean += m_CostRing[i];
            mean = m_CostCount > 0 ? mean / m_CostCount : 0.0;
            float seconds = m_TagWindowStart >= 0f ? UnityEngine.Time.realtimeSinceStartup - m_TagWindowStart : 0f;
            double tagsPerS = seconds > 0f ? m_TagTotal / seconds : 0.0;
            double writtenPerPass = m_SampledPasses > 0 ? (double)m_WrittenTotal / m_SampledPasses : 0.0;
            LiftMoverMath.PaceSettings pace = s_Pace;
            return string.Format(CultureInfo.InvariantCulture,
                "{0} [cost] chair mover ({1}; {2}{3}): main thread p50 {4:F3} / p95 {5:F3} / max {6:F3} ms per pass (mean {7:F3}, " +
                "{8} sample(s)) over {9} chair(s) in {10} loop(s); last pass {11} near / {12} far loop(s), {13:F0} chair(s) written " +
                "per pass on average; bounds refresh: {14} tag pass(es), mean {15:F3} ms each (collect + one batched AddComponent), " +
                "{16} Updated tag(s) in {17:F1} s = {18:F0} per second; far-loop pace {19}",
                nameof(ChairPinSystem), window, m_DiagMoverPath, s_StressFactor > 1 ? $", stress x{s_StressFactor}" : string.Empty,
                p50, p95, max, mean, m_CostCount, m_PassRiding, m_LoopCache.m_Loops.Count, m_PassNear, m_PassFar, writtenPerPass,
                m_BoundsCostPasses, m_BoundsCostPasses > 0 ? m_BoundsCostTicks * msPerTick / m_BoundsCostPasses : 0.0,
                m_TagTotal, seconds, tagsPerS,
                pace.Enabled
                    ? string.Format(CultureInfo.InvariantCulture, "on (>= {0:F0} m, stride <= {1}, step <= {2:F1} mrad)",
                                    pace.FarMinM, pace.MaxStride, pace.MaxStepRadians * 1000.0)
                    : "off");
        }
    }
}
