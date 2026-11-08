// Headless benchmark of the chair mover's math at release scale. Run from the repo root. It needs the game installed and CSII_MANAGEDPATH set:
//   dotnet run --project tests/MoverBench/MoverBench.csproj -c Release
// It prints INFO / BENCH lines, `FAIL  where: msg` lines and one SUMMARY line. Environment.ExitCode = failures.
// It builds a synthetic 20-lift resort (about 3,000 carriers over 6 x 6 km) through the real LiftLoopCache.Build. It runs on a single
// thread on the .NET 8 JIT, not Burst or the game's Mono. In-game numbers come from the dev tab.
//   B-1 correctness: LiftLoopSampler.Sample on the flat arrays == LiftLoopCache.Sample bit for bit, and Pose == MoveChair's pose, for
//       every chair at three clocks (Burst may contract float ops differently).
//   B-2 / B-3 the managed per-chair pass vs the flat MoveChairsJob kernel. The measure is p50 / p95 / max over 600 passes.
//       It runs one thread, then Parallel.For batch 64.
//   B-4 pace: four camera scenarios.
//   B-5 the carrier grip round (the brief's "B-2 case", numbered on). It runs 2,000 carriers with grips + swing (+ gust, 2026-11-06)
//       through the split kernel (one sample, two Transforms) and through the rigid kernel on the same carriers. It also tests correctness
//       (toggle off == the rigid pose, grip axis along the rope, zero swing on guided segments, the free-run marks).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading.Tasks;
using Colossal.Mathematics;
using Game.Net;
using Game.Pathfind;
using SkiResortMod.Systems;
using Unity.Entities;
using Unity.Mathematics;
using Transform = Game.Objects.Transform;

namespace MoverBench
{
    internal static class Program
    {
        private static int s_Checks, s_Failures;

        private struct ArraySegments : IFlatSegments
        {
            public FlatSegment[] A;
            public FlatSegment Get(int index) => A[index];
        }

        private struct ArrayStore : IFlatSegmentStore
        {
            public FlatSegment[] A;
            public FlatSegment Get(int index) => A[index];
            public void Set(int index, FlatSegment value) => A[index] = value;
        }

        private sealed class Lift
        {
            public LiftTypeDef Type;
            public float LineM, SpanM, SpacingM, Drop;
        }

        private struct Chair
        {
            public int Loop;
            public double Offset;
            public float Drop;
        }

        private static int Main()
        {
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            var rng = new System.Random(0x5CA1E0B1);
            List<LiftLoopCache.Loop> loops = BuildResort(rng, out int unclosed);
            Check(loops.Count == 20 && unclosed == 0, "bench/build", $"{loops.Count} loops closed, {unclosed} unclosed chains (want 20 / 0)");

            // Chairs on an even grid (as the rebuild's entity-index grid) and the flat arrays (as EnsureMoverArrays)
            var chairs = new List<Chair>();
            var flat = new List<FlatSegment>();
            var segFirst = new int[loops.Count];
            var boxMin = new float3[loops.Count];
            var boxMax = new float3[loops.Count];
            double totalLoopM = 0.0;
            for (int l = 0; l < loops.Count; l++)
            {
                LiftLoopCache.Loop loop = loops[l];
                totalLoopM += loop.m_Length;
                segFirst[l] = flat.Count;
                float3 mn = new float3(float.MaxValue), mx = new float3(float.MinValue);
                for (int s = 0; s < loop.m_Segments.Count; s++)
                {
                    FlatSegment f = LiftLoopSampler.Flatten(loop.m_Segments[s]);
                    LiftLoopSampler.GrowBox(f, ref mn, ref mx);
                    flat.Add(f);
                }
                boxMin[l] = mn - 8f;
                boxMax[l] = mx + 8f;
                int n = loop.m_Chairs.Count;
                for (int i = 0; i < n; i++)
                    chairs.Add(new Chair { Loop = l, Offset = loop.m_Offsets[i], Drop = loop.m_Drops[i] });
            }
            var segs = new ArraySegments { A = flat.ToArray() };
            Chair[] chairArray = chairs.ToArray();
            Console.WriteLine($"INFO  bench/resort: {loops.Count} loops, {totalLoopM / 1000.0:F1} km of loop, {chairArray.Length} carriers, " +
                              $"{flat.Count} segments");

            B1Correctness(loops, chairArray, segs, segFirst);
            B2B3Timing(loops, chairArray, segs, segFirst);
            B4Pace(loops, chairArray, boxMin, boxMax);
            B5Grips(loops, chairArray, segs, segFirst);

            Console.WriteLine($"SUMMARY  MoverBench checks={s_Checks} failures={s_Failures}");
            Environment.ExitCode = s_Failures;
            return s_Failures;
        }

        private static bool Check(bool cond, string where, string msg)
        {
            s_Checks++;
            if (!cond)
            {
                s_Failures++;
                Console.WriteLine("FAIL  " + where + ": " + msg);
            }
            return cond;
        }

        private static LiftTypeDef TypeById(string id)
        {
            for (int i = 0; i < SkiCatalog.Lifts.Count; i++)
                if (SkiCatalog.Lifts[i].Id == id)
                    return SkiCatalog.Lifts[i];
            throw new InvalidOperationException("no lift type " + id);
        }

        private static List<LiftLoopCache.Loop> BuildResort(System.Random rng, out int unclosed)
        {
            LiftTypeDef fg = TypeById("fixedGrip"), dq = TypeById("detachable"), gd = TypeById("gondola");
            var lifts = new List<Lift>();
            for (int i = 0; i < 6; i++) lifts.Add(new Lift { Type = fg, LineM = 1000f, SpanM = 60f, SpacingM = fg.SpacingM, Drop = 2.4f });
            for (int i = 0; i < 2; i++) lifts.Add(new Lift { Type = fg, LineM = 600f, SpanM = 60f, SpacingM = fg.SpacingM, Drop = 2.4f });
            for (int i = 0; i < 8; i++) lifts.Add(new Lift { Type = dq, LineM = 1200f, SpanM = 90f, SpacingM = dq.SpacingM, Drop = 3.3f });
            for (int i = 0; i < 4; i++) lifts.Add(new Lift { Type = gd, LineM = 2000f, SpanM = 200f, SpacingM = gd.SpacingM, Drop = 4.5f });

            var lanes = new List<LiftLoopCache.CableLane>();
            var stations = new List<Entity>();
            var stationPositions = new List<float3>();
            var stationByOwner = new Dictionary<int, int>();
            var wheels = new List<float3>();
            int nextId = 1;
            for (int li = 0; li < lifts.Count; li++)
            {
                Lift lift = lifts[li];
                float3 basePos = new float3((float)(rng.NextDouble() * 6000.0 - 3000.0), 800f, (float)(rng.NextDouble() * 6000.0 - 3000.0));
                float heading = (float)(rng.NextDouble() * math.PI * 2.0);
                float3 dir = new float3(math.cos(heading), 0f, math.sin(heading));
                float3 side = new float3(-dir.z, 0f, dir.x);
                float horizontal = lift.LineM / math.sqrt(1f + 0.35f * 0.35f);
                int spans = (int)math.ceil(lift.LineM / lift.SpanM);
                var baseStation = new Entity { Index = 100000 + nextId++, Version = 1 };
                var topStation = new Entity { Index = 100000 + nextId++, Version = 1 };
                stationByOwner[baseStation.Index] = stations.Count;
                stations.Add(baseStation);
                stationPositions.Add(basePos);
                wheels.Add(new float3(float.NaN));
                float3 topPos = basePos + dir * horizontal + new float3(0f, horizontal * 0.35f, 0f);
                stationByOwner[topStation.Index] = stations.Count;
                stations.Add(topStation);
                stationPositions.Add(topPos);
                wheels.Add(new float3(float.NaN));

                var towers = new Entity[spans + 1];
                for (int k = 0; k <= spans; k++)
                    towers[k] = k == 0 ? baseStation : k == spans ? topStation : new Entity { Index = nextId++, Version = 1 };
                for (int k = 0; k < spans; k++)
                {
                    var edge = new Entity { Index = nextId++, Version = 1 };
                    float t0 = (float)k / spans, t1 = (float)(k + 1) / spans;
                    float3 a = math.lerp(basePos, topPos, t0) + new float3(0f, 10f, 0f);
                    float3 d = math.lerp(basePos, topPos, t1) + new float3(0f, 10f, 0f);
                    float sag = 0.03f * lift.SpanM;
                    for (int s = 0; s < 2; s++)
                    {
                        float3 off = side * (s == 0 ? 1.8f : -1.8f);
                        float3 pa = a + off, pd = d + off;
                        var bez = new Bezier4x3(pa, math.lerp(pa, pd, 1f / 3f) - new float3(0f, sag, 0f),
                                                math.lerp(pa, pd, 2f / 3f) - new float3(0f, sag, 0f), pd);
                        lanes.Add(new LiftLoopCache.CableLane
                        {
                            m_Lane = new Entity { Index = nextId++, Version = 1 },
                            m_Owner = edge,
                            m_Bezier = bez,
                            m_Start = new PathNode(towers[k], (ushort)s),
                            m_End = new PathNode(towers[k + 1], (ushort)s),
                            m_Length = MathUtils.Length(bez),
                            m_TypeIndex = lift.Type.Index,
                        });
                    }
                }
            }

            var cache = new LiftLoopCache();
            cache.Build(lanes, stations, stationPositions, stationByOwner, wheels);
            unclosed = cache.m_UnclosedChainCount;
            var loops = new List<LiftLoopCache.Loop>(cache.m_Loops);
            for (int l = 0; l < loops.Count; l++)
            {
                LiftLoopCache.Loop loop = loops[l];
                LiftTypeDef def = SkiCatalog.LiftByIndex(loop.m_TypeIndex);
                loop.m_SpeedMps = def.SpeedMps;
                Lift lift = lifts[0];
                for (int li = 0; li < lifts.Count; li++)
                    if (lifts[li].Type == def) { lift = lifts[li]; break; }
                int n = (int)(loop.m_Length / def.SpacingM) - 2;   // fewer than the loop holds (EvenSpacing per edge and row)
                for (int i = 0; i < n; i++)
                {
                    loop.m_Chairs.Add(new Entity { Index = 500000 + i, Version = 1 });
                    loop.m_Drops.Add(lift.Drop);
                    loop.m_Offsets.Add(i * loop.m_Length / n);
                }
            }
            return loops;
        }

        private static void B1Correctness(List<LiftLoopCache.Loop> loops, Chair[] chairs, ArraySegments segs, int[] segFirst)
        {
            int badSample = 0, badPose = 0, compared = 0;
            foreach (double timeS in new[] { 0.0, 1234.5678, 7.16e7 / 3.0 })
            {
                for (int c = 0; c < chairs.Length; c++)
                {
                    Chair ch = chairs[c];
                    LiftLoopCache.Loop loop = loops[ch.Loop];
                    double s = LiftMoverMath.LoopPosition(ch.Offset, loop.m_SpeedMps, timeS, loop.m_Length);
                    LiftLoopCache.Sample(loop, s, out float3 p0, out float3 t0);
                    LiftLoopSampler.Sample(ref segs, segFirst[ch.Loop], loop.m_Segments.Count, s, out float3 p1, out float3 t1);
                    compared++;
                    if (!BitEq(p0, p1) || !BitEq(t0, t1))
                        badSample++;
                    // MoveChair's old managed pose vs LiftLoopSampler.Pose
                    var before = new Transform(new float3(1f, 2f, 3f), quaternion.identity);
                    var after = before;
                    float3 pos = p0;
                    pos.y -= ch.Drop;
                    quaternion rot = NetUtils.GetNodeRotation(t0, before.m_Rotation);
                    bool inPlace = math.distancesq(pos, before.m_Position) < 1e-6f && math.abs(math.dot(rot, before.m_Rotation)) > 0.99999f;
                    byte r = LiftLoopSampler.Pose(p1, t1, ch.Drop, ref after);
                    if (inPlace != (r == LiftLoopSampler.InPlace) || (!inPlace && (!BitEq(after.m_Position, pos) || !BitEq(after.m_Rotation.value, rot.value))))
                        badPose++;
                }
            }
            Check(badSample == 0, "bench/B-1", $"{badSample} of {compared} flat samples differ from LiftLoopCache.Sample (bit for bit)");
            Check(badPose == 0, "bench/B-1", $"{badPose} of {compared} poses differ from MoveChair's");
            Console.WriteLine($"INFO  bench/B-1: {compared} samples compared bit for bit (flat kernel == LiftLoopCache.Sample, Pose == MoveChair)");
        }

        private static bool BitEq(float3 a, float3 b) =>
            BitConverter.SingleToInt32Bits(a.x) == BitConverter.SingleToInt32Bits(b.x) &&
            BitConverter.SingleToInt32Bits(a.y) == BitConverter.SingleToInt32Bits(b.y) &&
            BitConverter.SingleToInt32Bits(a.z) == BitConverter.SingleToInt32Bits(b.z);

        private static bool BitEq(float4 a, float4 b) => BitEq(a.xyz, b.xyz) && BitConverter.SingleToInt32Bits(a.w) == BitConverter.SingleToInt32Bits(b.w);

        private const int Passes = 600, Warmup = 200;

        private static void B2B3Timing(List<LiftLoopCache.Loop> loops, Chair[] chairs, ArraySegments segs, int[] segFirst)
        {
            var transforms = new Transform[chairs.Length];
            for (int i = 0; i < transforms.Length; i++)
                transforms[i] = new Transform(float3.zero, quaternion.identity);
            var samples = new float[Passes];
            double dt = 1.0 / 60.0;

            // B-2: the old managed per-chair path (minus EntityManager)
            double timeS = 5000.0;
            for (int pass = -Warmup; pass < Passes; pass++)
            {
                timeS += dt;
                long t0 = Stopwatch.GetTimestamp();
                for (int c = 0; c < chairs.Length; c++)
                {
                    Chair ch = chairs[c];
                    LiftLoopCache.Loop loop = loops[ch.Loop];
                    double s = (ch.Offset + loop.m_SpeedMps * timeS) % loop.m_Length;
                    LiftLoopCache.Sample(loop, s, out float3 position, out float3 tangent);
                    position.y -= ch.Drop;
                    Transform t = transforms[c];
                    quaternion rotation = NetUtils.GetNodeRotation(tangent, t.m_Rotation);
                    if (math.distancesq(position, t.m_Position) < 1e-6f && math.abs(math.dot(rotation, t.m_Rotation)) > 0.99999f)
                        continue;
                    t.m_Position = position;
                    t.m_Rotation = rotation;
                    transforms[c] = t;
                }
                if (pass >= 0)
                    samples[pass] = Ms(t0);
            }
            Report("B-2 before (managed per-chair math, List<Segment>, all chairs every pass)", samples, chairs.Length);

            // B-3: the flat kernel, one thread
            for (int pass = -Warmup; pass < Passes; pass++)
            {
                timeS += dt;
                long t0 = Stopwatch.GetTimestamp();
                for (int c = 0; c < chairs.Length; c++)
                    Kernel(ref segs, loops, segFirst, chairs, transforms, c, timeS);
                if (pass >= 0)
                    samples[pass] = Ms(t0);
            }
            Report("B-3 after (flat kernel = MoveChairsJob.Execute's math, one thread, JIT)", samples, chairs.Length);

            // B-3b: the same kernel over worker threads, batch 64. This has the IJobParallelFor shape. The timing is wall time.
            int workers = Math.Max(1, Environment.ProcessorCount - 1);
            var opts = new ParallelOptions { MaxDegreeOfParallelism = workers };
            int batches = (chairs.Length + 63) / 64;
            for (int pass = -Warmup; pass < Passes; pass++)
            {
                timeS += dt;
                double now = timeS;
                long t0 = Stopwatch.GetTimestamp();
                Parallel.For(0, batches, opts, b =>
                {
                    var local = segs;
                    int end = Math.Min(chairs.Length, (b + 1) * 64);
                    for (int c = b * 64; c < end; c++)
                        Kernel(ref local, loops, segFirst, chairs, transforms, c, now);
                });
                if (pass >= 0)
                    samples[pass] = Ms(t0);
            }
            Report($"B-3b after (flat kernel over {workers} worker thread(s), batch 64, wall time)", samples, chairs.Length);
        }

        private static void Kernel(ref ArraySegments segs, List<LiftLoopCache.Loop> loops, int[] segFirst, Chair[] chairs,
            Transform[] transforms, int c, double timeS)
        {
            Chair ch = chairs[c];
            LiftLoopCache.Loop loop = loops[ch.Loop];
            double phase = loop.m_SpeedMps * timeS;
            double s = (ch.Offset + (phase + loop.m_SpeedMps * 0.0)) % loop.m_Length;
            LiftLoopSampler.Sample(ref segs, segFirst[ch.Loop], loop.m_Segments.Count, s, out float3 p, out float3 tan);
            Transform t = transforms[c];
            if (LiftLoopSampler.Pose(p, tan, ch.Drop, ref t) == LiftLoopSampler.Moved)
                transforms[c] = t;
        }

        // ---- B-5: the carrier grip round ----

        /// <summary>The per-loop swing inputs (ChairPinSystem.SwingInputs' twin).</summary>
        private struct LoopSwing
        {
            public CarrierPivots Pivots;
            public SwingLimits Limits;
            public float Omega, RampM, Speed;
        }

        /// <summary>The step-0 wind sample (SampleWind at the field-test city, units UNVERIFIED).</summary>
        private static readonly float2 BenchWind = new float2(0.31f, 0.05f);
        /// <summary>The stopped lift (DECISIONS 619): the timed split kernel runs with a held loop's sway shift and envelope (the scale branch
        /// taken), the job's per-carrier work for a held or a running loop.</summary>
        private const float BenchSwayShift = 1.3f, BenchSwayScale = 0.6f;

        private static void B5Grips(List<LiftLoopCache.Loop> loops, Chair[] chairs, ArraySegments segs, int[] segFirst)
        {
            const string W = "bench/B-5";
            // The free-run marks, as EnsureMoverArrays sets them.
            var store = new ArrayStore { A = (FlatSegment[])segs.A.Clone() };
            var swingOf = new LoopSwing[loops.Count];
            int guidedSegs = 0, loopsWithoutGuided = 0;
            for (int l = 0; l < loops.Count; l++)
            {
                int count = loops[l].m_Segments.Count;
                LiftLoopSampler.MarkRuns(ref store, segFirst[l], count);
                int g = 0;
                for (int s = 0; s < count; s++)
                    g += store.A[segFirst[l] + s].Guided;
                guidedSegs += g;
                if (g == 0)
                    loopsWithoutGuided++;
                LiftTypeDef def = SkiCatalog.LiftByIndex(loops[l].m_TypeIndex);
                CarrierPivots pv = def != null ? def.Pivots : default;
                swingOf[l] = new LoopSwing
                {
                    Pivots = pv,
                    Limits = def != null ? CarrierPose.Limits(pv, def.Mover == MoverKind.Shuttle) : default,
                    Omega = CarrierPose.Omega(pv.BodyPin.y),
                    RampM = CarrierPose.RampM((float)loops[l].m_SpeedMps),
                    Speed = (float)loops[l].m_SpeedMps,
                };
            }
            Check(loopsWithoutGuided == 0, W, $"{loopsWithoutGuided} loop(s) without a guided segment (arcs / terminal runs)");
            var marked = new ArraySegments { A = store.A };
            int n = Math.Min(2000, chairs.Length);

            // Correctness at three clocks:
            // - toggle off == the rigid pose (1e-4 m).
            // - the grip axis along the rope (0.1 deg).
            // - no swing on a guided segment.
            // - the swing within the clamps.
            int badRigid = 0, badAxis = 0, badJoint = 0, badGuided = 0, badClamp = 0, onGuided = 0, compared = 0, rolled = 0;
            foreach (double timeS in new[] { 0.0, 1234.5678, 7.16e7 / 3.0 })
            {
                for (int c = 0; c < n; c++)
                {
                    Chair ch = chairs[c];
                    LiftLoopCache.Loop loop = loops[ch.Loop];
                    LoopSwing ls = swingOf[ch.Loop];
                    double s = LiftMoverMath.LoopPosition(ch.Offset, loop.m_SpeedMps, timeS, loop.m_Length);
                    LiftLoopSampler.Sample(ref marked, segFirst[ch.Loop], loop.m_Segments.Count, s, out float3 p, out float3 tan,
                                           out int seg, out float d);
                    quaternion yaw = NetUtils.GetNodeRotation(tan, quaternion.identity);
                    FlatSegment f = marked.A[seg];
                    float2 ramp = CarrierPose.Ramp(CarrierPose.DistanceToGuided(f.Guided != 0, d, f.Length, f.RunBefore, f.RunAfter), ls.RampM);
                    float2 swing = CarrierPose.Swing(BenchWind, yaw, CarrierPose.SwayArg(s, ls.Omega, ls.Speed, CarrierPose.SeedPhase((uint)c)), ramp, ls.Limits,
                                                     CarrierPose.Gust(CarrierPose.GustClock(timeS), p));
                    compared++;
                    if (f.Guided != 0)
                    {
                        onGuided++;
                        if (swing.x != 0f || swing.y != 0f)
                            badGuided++;
                    }
                    if (math.abs(swing.x) > ls.Limits.MaxRoll || math.abs(swing.y) > CarrierPose.MaxForeAft)
                        badClamp++;
                    CarrierPose.Pose(p, tan, yaw, ls.Pivots, false, float2.zero, out _, out _, out float3 rigidBody, out quaternion rigidRot);
                    var t = new Transform(new float3(1f, 2f, 3f), quaternion.identity);
                    LiftLoopSampler.Pose(p, tan, ls.Pivots.RigidDrop, ref t);
                    if (math.distance(rigidBody, t.m_Position) > 1e-4f || math.abs(math.dot(rigidRot, t.m_Rotation)) < 0.99999f)
                        badRigid++;
                    // Under the full swing, the grip's axis stays along the rope (the roll turns about the rope tangent). The pin
                    // joint holds (1 mm: city coordinates, a float step of 0.1 mm or more).
                    CarrierPose.Pose(p, tan, yaw, ls.Pivots, true, swing, out float3 gripPos, out quaternion gripRot, out float3 bodyPos,
                                     out quaternion bodyRot);
                    float3 fwd = math.rotate(gripRot, new float3(0f, 0f, 1f));
                    if (math.degrees(math.acos(math.clamp(math.abs(math.dot(fwd, math.normalize(tan))), 0f, 1f))) > 0.1f)
                        badAxis++;
                    if (CarrierPose.PinGap(gripPos, gripRot, bodyPos, bodyRot, ls.Pivots) > 1e-3f)
                        badJoint++;
                    if (swing.x != 0f)
                        rolled++;
                }
            }
            Check(badRigid == 0, W, $"{badRigid} of {compared} toggle-off bodies off the rigid pose");
            Check(badAxis == 0 && rolled > 0, W, $"{badAxis} of {compared} grips off the rope axis by more than 0.1 deg ({rolled} rolled)");
            Check(badJoint == 0, W, $"{badJoint} of {compared} pin joints open by more than 1 mm under the swing");
            Check(badGuided == 0 && onGuided > 0, W, $"{badGuided} of {onGuided} carriers on a guided segment swing");
            Check(badClamp == 0, W, $"{badClamp} swings over the clamps");
            Console.WriteLine($"INFO  {W}: {compared} split poses checked ({onGuided} on guided segments, {guidedSegs} guided segment(s) " +
                              $"over {loops.Count} loops)");

            // Timing: the rigid kernel vs the split kernel (grips + swing) on the same n carriers.
            var bodies = new Transform[n];
            var grips = new Transform[n];
            var samples = new float[Passes];
            double dt = 1.0 / 60.0, clock = 9000.0;
            for (int pass = -Warmup; pass < Passes; pass++)
            {
                clock += dt;
                long t0 = Stopwatch.GetTimestamp();
                for (int c = 0; c < n; c++)
                    Kernel(ref segs, loops, segFirst, chairs, bodies, c, clock);
                if (pass >= 0)
                    samples[pass] = Ms(t0);
            }
            float rigidP50 = Report($"B-5 rigid (today's kernel, {n} carriers, one thread, JIT)", samples, n);
            for (int pass = -Warmup; pass < Passes; pass++)
            {
                clock += dt;
                long t0 = Stopwatch.GetTimestamp();
                float2 gustClock = CarrierPose.GustClock(clock);   // per loop and pass on the main thread in game (SwingInputs)
                for (int c = 0; c < n; c++)
                    SplitKernel(ref marked, loops, segFirst, chairs, swingOf, bodies, grips, c, clock, gustClock);
                if (pass >= 0)
                    samples[pass] = Ms(t0);
            }
            float splitP50 = Report($"B-5 split (grips + swing: one sample, two Transforms, {n} carriers, one thread, JIT)", samples, n);

            // The plan's budget (carrier-pivot-swing-plan section 4): grips + swing may add <= 0.5 ms of worker time at 2,000 carriers on
            // the mid-range estimate. The mid-range estimate = this machine's single-thread time x MidRangeFactor (UNVERIFIED factor). The bench
            // scales the result to 2,000 when the resort has fewer carriers. The bench runs JIT, not Burst: this is conservative. The main-thread
            // half (<= 0.1 ms: the wind poll, the grip tags) has no bench twin. The in-game [cost] line measures it.
            const float MidRangeFactor = 2f, WorkerBudgetMs = 0.5f;
            float addedMs = math.max(0f, splitP50 - rigidP50) * (2000f / n) * MidRangeFactor;
            Check(addedMs <= WorkerBudgetMs, W, string.Format(CultureInfo.InvariantCulture,
                "grips + swing add {0:F3} ms of worker time at 2,000 carriers on the mid-range estimate (budget {1:F1} ms)", addedMs, WorkerBudgetMs));
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "BENCH B-5 budget: grips + swing add {0:F3} ms per pass at 2,000 carriers on the mid-range estimate (x{1:F0}; budget {2:F1} ms worker)",
                addedMs, MidRangeFactor, WorkerBudgetMs));
        }

        /// <summary>MoveChairsJob.Execute's split branch (ChairPinSystem.Mover.cs), minus the ECS lookups.</summary>
        private static void SplitKernel(ref ArraySegments segs, List<LiftLoopCache.Loop> loops, int[] segFirst, Chair[] chairs,
            LoopSwing[] swingOf, Transform[] bodies, Transform[] grips, int c, double timeS, float2 gustClock)
        {
            Chair ch = chairs[c];
            LiftLoopCache.Loop loop = loops[ch.Loop];
            LoopSwing ls = swingOf[ch.Loop];
            double s = (ch.Offset + loop.m_SpeedMps * timeS) % loop.m_Length;
            LiftLoopSampler.Sample(ref segs, segFirst[ch.Loop], loop.m_Segments.Count, s, out float3 p, out float3 tan, out int seg, out float d);
            Transform body = bodies[c];
            quaternion yaw = NetUtils.GetNodeRotation(tan, body.m_Rotation);
            FlatSegment f = segs.A[seg];
            float2 ramp = CarrierPose.Ramp(CarrierPose.DistanceToGuided(f.Guided != 0, d, f.Length, f.RunBefore, f.RunAfter), ls.RampM);
            float2 swing = float2.zero;
            if (ramp.x > 0f || ramp.y > 0f)
                swing = CarrierPose.Swing(BenchWind, yaw, CarrierPose.SwayArg(s, ls.Omega, ls.Speed, 0.37f * c + BenchSwayShift), ramp, ls.Limits,
                                          CarrierPose.Gust(gustClock, p), BenchSwayScale);
            CarrierPose.Pose(p, tan, yaw, ls.Pivots, true, swing, out float3 gp, out quaternion gr, out float3 bp, out quaternion br);
            if (LiftLoopSampler.Write(bp, br, ref body) == LiftLoopSampler.Moved)
                bodies[c] = body;
            Transform grip = grips[c];
            if (LiftLoopSampler.Write(gp, gr, ref grip) == LiftLoopSampler.Moved)
                grips[c] = grip;
        }

        private static float Ms(long t0) => (float)((Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency);

        /// <summary>Prints one BENCH line and returns the p50 (ms).</summary>
        private static float Report(string what, float[] samples, int chairs)
        {
            var scratch = new List<float>();
            LiftMoverMath.Percentiles(samples, samples.Length, scratch, out float p50, out float p95, out float max);
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "BENCH {0}: p50 {1:F3} / p95 {2:F3} / max {3:F3} ms per pass over {4} chairs ({5:F3} us/chair at p50)",
                what, p50, p95, max, chairs, p50 * 1000.0 / chairs));
            return p50;
        }

        private static void B4Pace(List<LiftLoopCache.Loop> loops, Chair[] chairs, float3[] boxMin, float3[] boxMax)
        {
            var perLoop = new int[loops.Count];
            foreach (Chair ch in chairs)
                perLoop[ch.Loop]++;
            float3 center = float3.zero;
            for (int l = 0; l < loops.Count; l++)
                center += (boxMin[l] + boxMax[l]) * 0.5f;
            center /= loops.Count;
            float3 aBase = (boxMin[0] + boxMax[0]) * 0.5f;

            var scenarios = new List<(string name, Func<int, float3> camera)>
            {
                ("close-up: 150 m from lift 0's middle", p => aBase + new float3(0f, 150f, 0f)),
                ("mid zoom: 800 m above the resort center", p => center + new float3(0f, 800f, 0f)),
                ("overview: 3,000 m above the resort center", p => center + new float3(0f, 3000f, 0f)),
                ("flyover: 300 m up, crossing the resort in 600 passes", p => new float3(-3500f + 7000f * p / 600f, center.y + 300f, -3500f + 7000f * p / 600f)),
            };
            double maxSpeed = 0.0;
            foreach (var loop in loops)
                maxSpeed = Math.Max(maxSpeed, loop.m_SpeedMps);

            foreach (double simSpeed in new[] { 1.0, 3.0 })
            {
                double dt = simSpeed / 60.0;
                // Before: one global cadence at the fastest loop's speed, every rider tagged.
                double beforeTagsPerS = chairs.Length * maxSpeed * simSpeed / LiftMoverMath.BoundsRefreshMeters;
                foreach (var (name, camera) in scenarios)
                {
                    var paces = new LiftMoverMath.LoopPace[loops.Count];
                    for (int l = 0; l < loops.Count; l++)
                        paces[l] = LiftMoverMath.Seed(l, LiftMoverMath.MaxStrideDefault, 0.0);
                    var settings = LiftMoverMath.PaceSettings.Default;
                    long written = 0, tags = 0, nearSum = 0, farSum = 0;
                    int maxWritten = 0;
                    var decideMs = new float[600];
                    const int passes = 600;
                    double timeS = 0.0;
                    for (int pass = 0; pass < passes; pass++)
                    {
                        timeS += dt;
                        float3 cam = camera(pass);
                        long d0 = Stopwatch.GetTimestamp();
                        int w = 0;
                        for (int l = 0; l < loops.Count; l++)
                        {
                            float d = LiftMoverMath.BoxDistance(cam, boxMin[l], boxMax[l]);
                            int stride = LiftMoverMath.StrideFor(d, loops[l].m_SpeedMps, dt, settings);
                            LiftMoverMath.Decide(ref paces[l], stride, timeS, loops[l].m_SpeedMps, out bool write, out bool tag);
                            if (stride > 1) farSum++;
                            else nearSum++;
                            if (write) w += perLoop[l];
                            if (tag) tags += perLoop[l];
                        }
                        written += w;
                        decideMs[pass] = Ms(d0);
                        maxWritten = Math.Max(maxWritten, w);
                    }
                    double seconds = passes / 60.0;
                    LiftMoverMath.Percentiles(decideMs, passes, new List<float>(), out _, out float decideP95, out _);
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "BENCH B-4 pace, {0}x, {1}: loops near/far {2:F1} / {3:F1}; chairs written per pass {4:F0} mean, {5} max of {6}; " +
                        "bounds Updated tags per second {7:F0} (before R-3: {8:F0}); pace decisions for all loops p95 {9:F1} us per pass",
                        simSpeed, name, nearSum / (double)passes, farSum / (double)passes, written / (double)passes, maxWritten,
                        chairs.Length, tags / seconds, beforeTagsPerS, decideP95 * 1000.0));
                }
            }
        }
    }
}
