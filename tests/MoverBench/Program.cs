// MoverBench — headless benchmark of the chair mover's math at release scale (R-3 of
// docs/research/lifts/lift-architecture-review-2026-10-22.md; report docs/research/lifts/lift-scale-2026-10-22.md, DECISIONS 369).
// Run (from the ModularSkiResort root; needs the game installed, CSII_MANAGEDPATH set by the modding toolchain):
//   dotnet run --project tests/MoverBench/MoverBench.csproj -c Release
// Prints INFO / BENCH lines, `FAIL  where: msg` lines and one SUMMARY line; Environment.ExitCode = failures.
//
// WHAT IT BUILDS: a synthetic 20-lift resort through the REAL LiftLoopCache.Build (PathNode chaining, terminal classification,
// pairing, bullwheel arcs): 6 fixed-grip x 1.0 km + 2 x 0.6 km (60 m spans, 8 m spacing), 8 detachables x 1.2 km (90 m spans,
// 22.5 m), 4 gondolas x 2.0 km (200 m spans, 40 m), scattered over 6 x 6 km, 35 % grade, sagging spans. About 3,000 carriers.
//
// WHAT IT MEASURES (one thread, .NET 8 JIT — NOT Burst, NOT the game's Mono; the in-game numbers come from the dev tab):
//   B-1 CORRECTNESS: LiftLoopSampler.Sample over the flat arrays == LiftLoopCache.Sample bit for bit, and Pose == MoveChair's
//       pose, for every chair at three clocks (the Burst job's kernel is this code; Burst may contract float ops differently).
//   B-2 BEFORE: the managed per-chair math of the pre-R-3 pass (Sample on the List<Segment> + grip + GetNodeRotation + the 1 mm
//       test + a Transform store), every chair every pass: p50 / p95 / max ms over 600 passes. The game added 5 EntityManager
//       calls per chair on top (the field cost, 0.85 us/chair, is in the report).
//   B-3 AFTER, kernel only: the flat-array kernel (what MoveChairsJob runs per chair), every chair, one thread; then the same
//       spread over worker threads (Parallel.For, batch 64 — the shape of IJobParallelFor; wall time).
//   B-4 PACE: four camera scenarios (close-up at a base station, mid zoom, overview, a 600-pass flyover) at 60 fps 1x and 3x:
//       loops near / far, chairs written per pass, bounds Updated tags per second before (one global cadence at the fastest
//       loop's speed, every rider) and after (per loop, own speed, 2 m near / 2 m x stride far).

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

        private sealed class Lift
        {
            public LiftTypeDef Type;
            public float LineM, SpanM, SpacingM, GripTop;
        }

        private struct Chair
        {
            public int Loop;
            public double Offset;
            public float GripTop;
        }

        private static int Main()
        {
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            var rng = new System.Random(0x5CA1E0B1);
            List<LiftLoopCache.Loop> loops = BuildResort(rng, out int unclosed);
            Check(loops.Count == 20 && unclosed == 0, "bench/build", $"{loops.Count} loops closed, {unclosed} unclosed chains (want 20 / 0)");

            // Chairs (even grid, as the rebuild's entity-index grid) and the flat arrays (as EnsureMoverArrays).
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
                    chairs.Add(new Chair { Loop = l, Offset = loop.m_Offsets[i], GripTop = loop.m_GripTops[i] });
            }
            var segs = new ArraySegments { A = flat.ToArray() };
            Chair[] chairArray = chairs.ToArray();
            Console.WriteLine($"INFO  bench/resort: {loops.Count} loops, {totalLoopM / 1000.0:F1} km of loop, {chairArray.Length} carriers, " +
                              $"{flat.Count} segments");

            B1Correctness(loops, chairArray, segs, segFirst);
            B2B3Timing(loops, chairArray, segs, segFirst);
            B4Pace(loops, chairArray, boxMin, boxMax);

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

        // ── the resort ──────────────────────────────────────────────────────────────────────────────────────────────────────
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
            for (int i = 0; i < 6; i++) lifts.Add(new Lift { Type = fg, LineM = 1000f, SpanM = 60f, SpacingM = fg.SpacingM, GripTop = 2.4f });
            for (int i = 0; i < 2; i++) lifts.Add(new Lift { Type = fg, LineM = 600f, SpanM = 60f, SpacingM = fg.SpacingM, GripTop = 2.4f });
            for (int i = 0; i < 8; i++) lifts.Add(new Lift { Type = dq, LineM = 1200f, SpanM = 90f, SpacingM = dq.SpacingM, GripTop = 3.3f });
            for (int i = 0; i < 4; i++) lifts.Add(new Lift { Type = gd, LineM = 2000f, SpanM = 200f, SpacingM = gd.SpacingM, GripTop = 4.5f });

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
                    loop.m_GripTops.Add(lift.GripTop);
                    loop.m_Offsets.Add(i * loop.m_Length / n);
                }
            }
            return loops;
        }

        // ── B-1 ─────────────────────────────────────────────────────────────────────────────────────────────────────────────
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
                    // MoveChair's pose (pre-R-3 managed) vs LiftLoopSampler.Pose.
                    var before = new Transform(new float3(1f, 2f, 3f), quaternion.identity);
                    var after = before;
                    float3 pos = p0;
                    pos.y -= ch.GripTop;
                    quaternion rot = NetUtils.GetNodeRotation(t0, before.m_Rotation);
                    bool inPlace = math.distancesq(pos, before.m_Position) < 1e-6f && math.abs(math.dot(rot, before.m_Rotation)) > 0.99999f;
                    byte r = LiftLoopSampler.Pose(p1, t1, ch.GripTop, ref after);
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

        // ── B-2 / B-3 ───────────────────────────────────────────────────────────────────────────────────────────────────────
        private const int Passes = 600, Warmup = 200;

        private static void B2B3Timing(List<LiftLoopCache.Loop> loops, Chair[] chairs, ArraySegments segs, int[] segFirst)
        {
            var transforms = new Transform[chairs.Length];
            for (int i = 0; i < transforms.Length; i++)
                transforms[i] = new Transform(float3.zero, quaternion.identity);
            var samples = new float[Passes];
            double dt = 1.0 / 60.0;

            // B-2: before — the managed per-chair path (minus EntityManager).
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
                    position.y -= ch.GripTop;
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

            // B-3: after — the flat kernel, one thread.
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

            // B-3b: the same kernel over worker threads, batch 64 (the IJobParallelFor shape; wall time).
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
            if (LiftLoopSampler.Pose(p, tan, ch.GripTop, ref t) == LiftLoopSampler.Moved)
                transforms[c] = t;
        }

        private static float Ms(long t0) => (float)((Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency);

        private static void Report(string what, float[] samples, int chairs)
        {
            var scratch = new List<float>();
            LiftMoverMath.Percentiles(samples, samples.Length, scratch, out float p50, out float p95, out float max);
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "BENCH {0}: p50 {1:F3} / p95 {2:F3} / max {3:F3} ms per pass over {4} chairs ({5:F3} us/chair at p50)",
                what, p50, p95, max, chairs, p50 * 1000.0 / chairs));
        }

        // ── B-4 ─────────────────────────────────────────────────────────────────────────────────────────────────────────────
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
