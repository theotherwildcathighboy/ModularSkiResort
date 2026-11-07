// The per-frame chair move pass at release scale: under 0.5 ms a frame on the main thread for 20 lifts / ~3,000 chairs.
// Running loops are flattened once into persistent native arrays (EnsureMoverArrays) and moved by MoveChairsJob (Burst, IJobParallelFor).
// Threading: each job index writes a different chair's Transform. The job handle goes into Dependency. The bounds-tag structural change
// runs BEFORE the job is scheduled. The native arrays are written on the main thread only after MoverCompleteJob.
// Timing: Modification4B. Vanilla 4B systems and ModificationBarrier4B run after it (SystemOrder.cs:83,:91).
// Held loops, Temp-chair previews and the static flow stay managed. Far loops (>= 500 m from the camera) are paced (LiftMoverMath).
// Engine notes: EngineNotes/ChairPinSystem.Mover.md

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
        // Settings. They are static and per app run, like MovingChairs. The dev tab flips them.
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
            // The carrier grip round (ChairPinSystem.Grips.cs): the type's pivots, the pass's blended wind, the pendulum frequency, the
            // ramp length and the flags. Split = the loop has paired grips. PitchSwing = the Options toggle "Animated lift carriers" (pitch
            // + swing). It is not named Animated: that is a Game.Rendering component, and the surface gate reads names. GripWrite = 0 when
            // a fallback hook skips the grips this pass (the bodies still move).
            public CarrierPivots Pivots;
            public SwingLimits Limits;   // the type's roll clamp and gain (CarrierPose.Limits)
            public float2 Wind;
            public float2 GustClock;    // CarrierPose.GustClock of the loop's gust time (CarrierWind.GustTimeS)
            public float Omega, RampM, SpeedF;
            public byte Split, PitchSwing, GripWrite;
        }

        private struct JobChair
        {
            public Entity Chair;
            public Entity Grip;    // Entity.Null = rigid one-piece pose (LiftLoopSampler.Pose, unchanged)
            public double Offset;
            public float Drop;
            public float Phase;    // CarrierPose.SeedPhase of the body entity
            public int Loop;
        }

        private struct NativeSegments : IFlatSegments
        {
            public NativeArray<FlatSegment> Array;
            public FlatSegment Get(int index) => Array[index];
        }

        /// <summary>EnsureMoverArrays' writable view for LiftLoopSampler.MarkRuns.</summary>
        private struct NativeSegmentList : IFlatSegmentStore
        {
            public NativeList<FlatSegment> List;
            public FlatSegment Get(int index) => List[index];
            public void Set(int index, FlatSegment value) => List[index] = value;
        }

        /// <summary>One index = one rostered chair (distinct entities: see EngineNotes/ChairPinSystem.Mover.md, "Threading").</summary>
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
            /// <summary>Per chair index: its grip's result (Skipped when it has none or the loop skips grips this pass).</summary>
            public NativeArray<byte> GripResults;
            public double ShiftS;   // stress mode only. 0.0 = the real pass (phase + speed × 0.0 == phase exactly)
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
                GripResults[index] = LiftLoopSampler.Skipped;   // first: the list is ResizeUninitialized, and every return below counts it
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
                LiftLoopSampler.Sample(ref segments, loop.SegFirst, loop.SegCount, s, out float3 position, out float3 tangent,
                                       out int segment, out float d);
                if (loop.Split == 0 || chair.Grip == Entity.Null)
                {
                    // Today's one-piece carrier, unchanged (MoverBench B-1).
                    byte result = LiftLoopSampler.Pose(position, tangent, chair.Drop, ref transform);
                    if (result == LiftLoopSampler.Moved)
                        Transforms[e] = transform;
                    Results[index] = result;
                    return;
                }

                // The split carrier: ONE sample, two Transforms (CarrierPose: the body's yaw is the rigid pose's).
                quaternion yaw = Game.Net.NetUtils.GetNodeRotation(tangent, transform.m_Rotation);
                float2 swing = float2.zero;
                if (loop.PitchSwing != 0)
                {
                    FlatSegment seg = Segments[segment];
                    float distance = CarrierPose.DistanceToGuided(seg.Guided != 0, d, seg.Length, seg.RunBefore, seg.RunAfter);
                    float2 ramp = CarrierPose.Ramp(distance, loop.RampM);
                    if (ramp.x > 0f || ramp.y > 0f)
                        swing = CarrierPose.Swing(loop.Wind, yaw, CarrierPose.SwayArg(s, loop.Omega, loop.SpeedF, chair.Phase), ramp, loop.Limits,
                                                  CarrierPose.Gust(loop.GustClock, position));
                }
                CarrierPose.Pose(position, tangent, yaw, loop.Pivots, loop.PitchSwing != 0, swing,
                                 out float3 gripPosition, out quaternion gripRotation, out float3 bodyPosition, out quaternion bodyRotation);
                byte bodyResult = LiftLoopSampler.Write(bodyPosition, bodyRotation, ref transform);
                if (bodyResult == LiftLoopSampler.Moved)
                    Transforms[e] = transform;
                Results[index] = bodyResult;

                if (loop.GripWrite == 0)
                    return;
                Entity g = chair.Grip;
                if (!Storage.Exists(g) || Deleted.HasComponent(g) || !Transforms.TryGetComponent(g, out Transform gripTransform))
                {
                    GripResults[index] = LiftLoopSampler.Missing;
                    return;
                }
                byte gripResult = LiftLoopSampler.Write(gripPosition, gripRotation, ref gripTransform);
                if (gripResult == LiftLoopSampler.Moved)
                    Transforms[g] = gripTransform;
                GripResults[index] = gripResult;
            }
        }

        /// <summary>Folds the per-chair results into Counts[0..3] (Skipped, Moved, InPlace, Missing) and the grips' into Counts[4..7].</summary>
        [BurstCompile]
        private struct SumResultsJob : IJob
        {
            [ReadOnly] public NativeArray<byte> Results;
            [ReadOnly] public NativeArray<byte> GripResults;
            public NativeArray<int> Counts;

            public void Execute()
            {
                Fold(Results, 0);
                Fold(GripResults, 4);
            }

            private void Fold(NativeArray<byte> results, int at)
            {
                int c0 = 0, c1 = 0, c2 = 0, c3 = 0;
                for (int i = 0; i < results.Length; i++)
                {
                    switch (results[i])
                    {
                        case LiftLoopSampler.Moved: c1++; break;
                        case LiftLoopSampler.InPlace: c2++; break;
                        case LiftLoopSampler.Missing: c3++; break;
                        default: c0++; break;
                    }
                }
                Counts[at] = c0;
                Counts[at + 1] = c1;
                Counts[at + 2] = c2;
                Counts[at + 3] = c3;
            }
        }

        private NativeList<FlatSegment> m_JobSegments;
        private NativeList<JobLoop> m_JobLoops;
        private NativeList<JobChair> m_JobChairs;
        private NativeList<byte> m_JobResults;
        private NativeList<byte> m_JobGripResults;   // per chair index, its grip's result (the carrier grip round)
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
            m_JobGripResults = new NativeList<byte>(512, Allocator.Persistent);
            m_JobCounts = new NativeArray<int>(8, Allocator.Persistent);
            m_ManagedProbe = new NativeArray<int>(1, Allocator.Persistent);
            m_MoverAllocated = true;
            m_Camera = World.GetOrCreateSystemManaged<CameraUpdateSystem>();
            GripsOnCreate();   // the wind map (ChairPinSystem.Grips.cs)
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
                m_JobGripMoved = m_JobCounts[5];
                m_JobGripInPlace = m_JobCounts[6];
                m_JobGripMissing = m_JobCounts[7];
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
            GripsReset();      // wind states, pairing index, grip counts (ChairPinSystem.Grips.cs)
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
                    m_JobGripResults.Dispose();
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
                // The free runs around each segment (the swing ramp and the carrier grip round).
                var store = new NativeSegmentList { List = m_JobSegments };
                LiftLoopSampler.MarkRuns(ref store, first, loop.m_Segments.Count);
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
                int n = math.min(loop.m_Chairs.Count, math.min(loop.m_Offsets.Count, loop.m_Drops.Count));
                for (int i = 0; i < n; i++)
                {
                    m_JobChairs.Add(new JobChair
                    {
                        Chair = loop.m_Chairs[i],
                        Grip = i < loop.m_Grips.Count ? loop.m_Grips[i] : Entity.Null,
                        Offset = loop.m_Offsets[i],
                        Drop = loop.m_Drops[i],
                        Phase = i < loop.m_SwayPhases.Count ? loop.m_SwayPhases[i] : 0f,
                        Loop = l,
                    });
                }
            }
            m_JobResults.ResizeUninitialized(m_JobChairs.Length);
            m_JobGripResults.ResizeUninitialized(m_JobChairs.Length);
        }

        /// <summary>One moving pass (EngineNotes/ChairPinSystem.Mover.md, "What runs when"). Called by UpdateMovingChairs after the pause test, with this
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
            // The carrier grip round: the toggle (one read per pass), the wind poll (every WindPollFrames, and at a rebuild), fallback (3).
            bool animated = AnimatedOn;
            PollWind(timeS, rebuilt);
            m_GripHalfOdd = !m_GripHalfOdd;
            bool gripPass = !GripHalfRate || m_GripHalfOdd || rebuilt;
            int gripsRiding = 0, gripsWritten = 0, gripTags = 0;
            bool anyActive = false;
            long boundsTicks = 0;

            // 1. last frame's job (completed in OnUpdate): its counts are this pass's "last pass" numbers in job mode.
            if (m_JobCountsFresh)
            {
                m_JobCountsFresh = false;
                if (m_JobMissing > 0 || m_JobGripMissing > 0)
                    m_LoopCacheDirty = true;   // a roster chair (or its grip) died: heal at the next rebuild (as MoveChair's Missing did)
            }

            var tagChairs = new NativeList<Entity>(64, Allocator.Temp);
            try
            {
                for (int l = 0; l < m_LoopCache.m_Loops.Count; l++)
                {
                    LiftLoopCache.Loop loop = m_LoopCache.m_Loops[l];
                    riding += loop.m_Chairs.Count;
                    gripsRiding += loop.m_GripCount;
                    JobLoop jl = l < m_JobLoops.Length ? m_JobLoops[l] : default;
                    jl.Active = 0;

                    // 2a. A HELD loop is written only at a rebuild or the hold's entry, at the frozen pose (LiftLoopHold.FrozenPhase =
                    // PhaseOf's product at FrozenTimeS, bit for bit). It is managed. Its entry tag is below.
                    if (m_HeldLoopCount > 0 && l < m_HoldByLoop.Count && m_HoldByLoop[l].Held)
                    {
                        LoopHold hold = m_HoldByLoop[l];
                        if (rebuilt || hold.PoseDirty)
                        {
                            double frozen = LiftLoopHold.FrozenPhase(loop.m_SpeedMps, hold.FrozenTimeS);
                            for (int i = 0; i < loop.m_Chairs.Count; i++)
                            {
                                double s = (loop.m_Offsets[i] + frozen) % loop.m_Length;
                                switch (MoveCarrier(loop, i, s, hold.FrozenTimeS, animated, writeGrip: true))
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
                            int beforeGrips = tagChairs.Length;
                            CollectGripsToTag(loop, tagChairs);
                            gripTags += tagChairs.Length - beforeGrips;
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

                    // 2b. the pace (EngineNotes/LiftMoverMath.md item 2).
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
                    // Fallback (1): a far loop's grips keep their last pose (and get no bounds tag) when GripNearOnly is on.
                    bool gripWrite = loop.m_GripCount > 0 && gripPass && !(GripNearOnly && stride > 1);
                    if (gripWrite)
                        gripsWritten += loop.m_GripCount;
                    if (tag)
                    {
                        long c0 = Stopwatch.GetTimestamp();
                        int before = tagChairs.Length;
                        CollectChairsToTag(loop, tagChairs);
                        if (gripWrite)
                        {
                            int beforeGrips = tagChairs.Length;
                            CollectGripsToTag(loop, tagChairs);
                            gripTags += tagChairs.Length - beforeGrips;
                        }
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
                        jl.Split = (byte)(loop.m_GripCount > 0 ? 1 : 0);
                        jl.PitchSwing = (byte)(animated ? 1 : 0);
                        jl.GripWrite = (byte)(gripWrite ? 1 : 0);
                        if (loop.m_GripCount > 0)
                        {
                            SwingInputs(loop, timeS, out jl.Pivots, out jl.Omega, out jl.RampM, out jl.Wind, out jl.Limits, out jl.GustClock);
                            jl.SpeedF = (float)loop.m_SpeedMps;
                        }
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
                                MoveResult r = MoveCarrier(loop, i, s, timeS, animated, gripWrite);
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

                // 3. the bounds tags: ONE structural change, BEFORE the job (EngineNotes/ChairPinSystem.Mover.md, "Threading").
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
                            GripResults = m_JobGripResults.AsArray(),
                            ShiftS = (k - (stress - 1)) * StressShiftS,
                            ManagedProbe = m_ManagedProbe,
                        };
                        handle = job.Schedule(m_JobChairs.Length, 64, handle);
                    }
                    handle = new SumResultsJob { Results = m_JobResults.AsArray(), GripResults = m_JobGripResults.AsArray(), Counts = m_JobCounts }.Schedule(handle);
                    Dependency = handle;
                    m_MoveHandle = handle;
                    m_MoveScheduled = true;
                    if (s_SyncMeasure)
                        MoverCompleteJob();   // measured inside the span below. The counts are this pass's
                }
                else
                {
                    m_JobMoved = m_JobInPlace = m_JobMissing = 0;   // nothing written this pass (every loop far and not due)
                    m_JobGripMoved = m_JobGripInPlace = m_JobGripMissing = 0;
                }
                // The counts of the last COMPLETED job (one pass old unless sync-measure is on).
                moved = m_JobMoved;
                inPlace = m_JobInPlace;
                missing += m_JobMissing;
                if (s_SyncMeasure && m_JobCountsFresh)
                {
                    m_JobCountsFresh = false;
                    if (m_JobMissing > 0 || m_JobGripMissing > 0)
                        m_LoopCacheDirty = true;
                }
            }
            m_DiagMoverPath = useJob ? (s_SyncMeasure ? "Burst job (sync-measure)" : "Burst job") : "managed";

            long moverTicks = Stopwatch.GetTimestamp() - moverStart - boundsTicks;
            if (!useJob && missing > 0)
                m_LoopCacheDirty = true; // roster chair died mid-window: heal next rebuild

            // Diagnostics (plain fields, the dev tab formats them on read).
            m_PassNear = near;
            m_PassFar = far;
            m_PassWritten = written;
            m_PassTags = tags;
            m_PassRiding = riding;
            m_PassGrips = gripsRiding;
            m_PassGripWritten = gripsWritten;
            m_PassGripTags = gripTags;
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
                m_GripWrittenTotal += gripsWritten;
                m_GripTagTotal += gripTags;
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
            m_GripWrittenTotal = 0;
            m_GripTagTotal = 0;
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
            double gripWrittenPerPass = m_SampledPasses > 0 ? (double)m_GripWrittenTotal / m_SampledPasses : 0.0;
            LiftMoverMath.PaceSettings pace = s_Pace;
            return string.Format(CultureInfo.InvariantCulture,
                "{0} [cost] chair mover ({1}; {2}{3}): main thread p50 {4:F3} / p95 {5:F3} / max {6:F3} ms per pass (mean {7:F3}, " +
                "{8} sample(s)) over {9} chair(s) in {10} loop(s); last pass {11} near / {12} far loop(s), {13:F0} chair(s) written " +
                "per pass on average; bounds refresh: {14} tag pass(es), mean {15:F3} ms each (collect + one batched AddComponent), " +
                "{16} Updated tag(s) in {17:F1} s = {18:F0} per second; far-loop pace {19}; grips (the carrier grip round, counted " +
                "apart): {20} paired, {21:F0} written per pass on average, {22} of the Updated tags; animated {23}{24}",
                nameof(ChairPinSystem), window, m_DiagMoverPath, s_StressFactor > 1 ? $", stress x{s_StressFactor}" : string.Empty,
                p50, p95, max, mean, m_CostCount, m_PassRiding, m_LoopCache.m_Loops.Count, m_PassNear, m_PassFar, writtenPerPass,
                m_BoundsCostPasses, m_BoundsCostPasses > 0 ? m_BoundsCostTicks * msPerTick / m_BoundsCostPasses : 0.0,
                m_TagTotal, seconds, tagsPerS,
                pace.Enabled
                    ? string.Format(CultureInfo.InvariantCulture, "on (>= {0:F0} m, stride <= {1}, step <= {2:F1} mrad)",
                                    pace.FarMinM, pace.MaxStride, pace.MaxStepRadians * 1000.0)
                    : "off",
                m_PassGrips, gripWrittenPerPass, m_GripTagTotal, AnimatedOn ? "on" : "off",
                GripNearOnly || GripHalfRate
                    ? " (fallback: " + (GripNearOnly ? "near only" : "") + (GripNearOnly && GripHalfRate ? ", " : "") + (GripHalfRate ? "half rate" : "") + ")"
                    : "");
        }
    }
}
