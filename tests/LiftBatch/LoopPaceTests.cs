// LoopPaceTests — R-3 / R-4 of the lift scale round (docs/research/lifts/lift-scale-2026-10-22.md, DECISIONS 369): the PURE
// arithmetic of LiftMoverMath.cs (far-loop pace, bounds cadence, cost percentiles) and LiftLaneScope (the scoped lane harvest's
// owners and the load-time watchdog compare). Where-tags `loop-pace/…`.
//   MP-1  LoopPosition == the mover's managed expression (offset + speed × TimeS) % L and the Burst job's
//         (offset + (phase + speed × 0.0)) % L, bit for bit, 20,000 seeded cases (clock up to the whole uint frame range / 60).
//   MP-2  BoxDistance: 0 inside / on the faces, axis distance on a face side, Euclidean at a corner.
//   MP-3  StrideFor: 1 when off, nearer than FarMinM, not moving, clock not advancing, NaN; worked examples (1 km gondola
//         at 60 fps 1x = 5, fixed-grip = 8 (cap), 3x speed = 1, 2 km = 8); 10,000 seeded cases: k × v × dt ≤ the allowed step
//         and (k + 1) × v × dt > it unless k is the cap.
//   MP-4  Decide at a constant stride k: written exactly every k passes after the seed's offset; stride 1 writes every pass.
//   MP-5  CATCH-UP: 20,000 passes, camera distance and frame time changing in spells: every near pass writes the exact
//         unthrottled pose (bit for bit), the written pose is never more than MaxStride − 1 passes old, and within a spell of
//         constant dt and distance a far chair's shown place is within MaxStepRadians × distance of the exact one.
//   MP-6  BOUNDS: near loops tag at ≥ 2 m of travel and are never ≥ 2 m + one pass of travel stale; far loops tag only on a
//         written pass and at ≥ 2 m × stride; the pass a loop comes back near with ≥ 2 m of untagged travel tags it.
//   MP-7  Tag rate: a fixed-grip loop near the camera at 60 fps, 1x, tags 1.20-1.25 times a second per chair (2.5 m/s / 2 m,
//         whole passes); far at
//         stride 8, at most 1/8 of that.
//   MP-8  Percentiles (nearest rank) on known samples; Rank bounds.
//   MP-9  LiftLaneScope.OwnersOf: every edge and its two nodes once, edge order, shared nodes deduplicated, null ids skipped.
//   MP-10 Scoped harvest vs full scan on a synthetic city (lift edges + nodes with HV cable lanes, a vanilla HV line, water
//         lanes, a Temp / Deleted lane): identical sets; a lane on a lift node no census edge touches is reported MISSED by
//         CompareHarvests; a lane the full filter rejects but a drifted scoped filter keeps is reported EXTRA.

using System;
using System.Collections.Generic;
using System.Globalization;
using SkiResortMod.Systems;
using Unity.Mathematics;

namespace LiftBatch
{
    public static class LoopPaceTests
    {
        private static Func<bool, string, string, bool> s_Check;
        private static readonly double[] s_Speeds = { 2.5, 5.0, 5.5 };   // the Loop types' SpeedMps (fixed-grip, detachable, gondola)

        public static void Run(Func<bool, string, string, bool> check)
        {
            s_Check = check;
            Mp1LoopPosition();
            Mp2BoxDistance();
            Mp3Stride();
            Mp4ConstantStride();
            Mp5CatchUp();
            Mp6Bounds();
            Mp7TagRate();
            Mp8Percentiles();
            Mp9Owners();
            Mp10Harvest();
        }

        private static bool Check(bool cond, string where, string msg) => s_Check(cond, where, msg);

        private static string F(double v) => v.ToString("R", CultureInfo.InvariantCulture);

        // ── MP-1 ────────────────────────────────────────────────────────────────────────────────────────────────────────────
        private static void Mp1LoopPosition()
        {
            const string W = "loop-pace/MP-1";
            var rng = new System.Random(0x5CA1E001);
            int bad = 0, badJob = 0;
            for (int n = 0; n < 20000; n++)
            {
                double length = 50.0 + rng.NextDouble() * 9950.0;
                double offset = rng.NextDouble() * length;
                double speed = s_Speeds[n % s_Speeds.Length];
                double timeS = rng.NextDouble() * (n % 2 == 0 ? 2e5 : 7.16e7);
                double phase = speed * timeS;                       // ChairPinSystem.PhaseOf
                double managed = (offset + phase) % length;         // RunMovePass's managed path / the held formula
                double job = (offset + (phase + speed * 0.0)) % length;   // MoveChairsJob with ShiftS = 0.0
                double pure = LiftMoverMath.LoopPosition(offset, speed, timeS, length);
                if (BitConverter.DoubleToInt64Bits(pure) != BitConverter.DoubleToInt64Bits(managed))
                    bad++;
                if (BitConverter.DoubleToInt64Bits(job) != BitConverter.DoubleToInt64Bits(managed))
                    badJob++;
            }
            Check(bad == 0, W, $"{bad} of 20000 LoopPosition values differ from (offset + speed × t) % L");
            Check(badJob == 0, W, $"{badJob} of 20000 job expressions (ShiftS 0) differ from the managed one");
        }

        // ── MP-2 ────────────────────────────────────────────────────────────────────────────────────────────────────────────
        private static void Mp2BoxDistance()
        {
            const string W = "loop-pace/MP-2";
            var min = new float3(0f, 0f, 0f);
            var max = new float3(10f, 20f, 30f);
            Check(LiftMoverMath.BoxDistance(new float3(5f, 5f, 5f), min, max) == 0f, W, "inside → 0");
            Check(LiftMoverMath.BoxDistance(new float3(10f, 20f, 30f), min, max) == 0f, W, "on the max corner → 0");
            Check(math.abs(LiftMoverMath.BoxDistance(new float3(15f, 5f, 5f), min, max) - 5f) < 1e-5f, W, "+x face side → 5");
            Check(math.abs(LiftMoverMath.BoxDistance(new float3(5f, -7f, 5f), min, max) - 7f) < 1e-5f, W, "−y face side → 7");
            Check(math.abs(LiftMoverMath.BoxDistance(new float3(13f, 24f, 30f), min, max) - 5f) < 1e-5f, W, "corner 3-4-5 → 5");
        }

        // ── MP-3 ────────────────────────────────────────────────────────────────────────────────────────────────────────────
        private static void Mp3Stride()
        {
            const string W = "loop-pace/MP-3";
            var on = LiftMoverMath.PaceSettings.Default;
            var off = on;
            off.Enabled = false;
            const double dt60 = 1.0 / 60.0;
            Check(LiftMoverMath.StrideFor(5000f, 2.5, dt60, off) == 1, W, "pace off → 1");
            Check(LiftMoverMath.StrideFor(499.9f, 2.5, dt60, on) == 1, W, "nearer than 500 m → 1");
            Check(LiftMoverMath.StrideFor(5000f, 0.0, dt60, on) == 1, W, "not moving → 1");
            Check(LiftMoverMath.StrideFor(5000f, 2.5, 0.0, on) == 1, W, "clock not advancing (paused / first pass) → 1");
            Check(LiftMoverMath.StrideFor(5000f, 2.5, -0.1, on) == 1, W, "clock stepping back → 1");
            Check(LiftMoverMath.StrideFor(float.NaN, 2.5, dt60, on) == 1, W, "NaN distance → 1");
            Check(LiftMoverMath.StrideFor(1000f, 5.5, dt60, on) == 5, W,
                $"1 km gondola, 60 fps 1x: want 5, got {LiftMoverMath.StrideFor(1000f, 5.5, dt60, on)}");
            Check(LiftMoverMath.StrideFor(1000f, 2.5, dt60, on) == 8, W, "1 km fixed-grip, 60 fps 1x: 12 → the cap 8");
            Check(LiftMoverMath.StrideFor(1000f, 5.5, 3.0 * dt60, on) == 1, W, "1 km gondola at 3x: 1.8 → 1");
            Check(LiftMoverMath.StrideFor(2000f, 5.5, dt60, on) == 8, W, "2 km gondola, 60 fps 1x: 10.9 → 8");
            Check(LiftMoverMath.StrideFor(500f, 2.5, 1.0 / 144.0, on) == 8, W, "500 m fixed-grip at 144 fps: 14.4 → 8");

            var rng = new System.Random(0x5CA1E003);
            int bad = 0;
            for (int n = 0; n < 10000; n++)
            {
                float d = 500f + (float)(rng.NextDouble() * 4500.0);
                double v = s_Speeds[n % s_Speeds.Length];
                double dt = (1.0 / 240.0 + rng.NextDouble() * (1.0 / 15.0)) * (1 + rng.Next(3));
                int k = LiftMoverMath.StrideFor(d, v, dt, on);
                double allowed = d * on.MaxStepRadians;
                bool within = k == 1 || k * v * dt <= allowed;
                bool maximal = k == on.MaxStride || (k + 1) * v * dt > allowed;
                if (k < 1 || k > on.MaxStride || !within || !maximal)
                    bad++;
            }
            Check(bad == 0, W, $"{bad} of 10000 strides break the step bound or are not maximal");
        }

        // ── MP-4 ────────────────────────────────────────────────────────────────────────────────────────────────────────────
        private static void Mp4ConstantStride()
        {
            const string W = "loop-pace/MP-4";
            for (int k = 1; k <= 8; k++)
            {
                for (int loopIndex = 0; loopIndex < 5; loopIndex++)
                {
                    LiftMoverMath.LoopPace pace = LiftMoverMath.Seed(loopIndex, 8, 0.0);
                    int seed = pace.SinceWrite;
                    var writes = new List<int>();
                    for (int pass = 1; pass <= 200; pass++)
                    {
                        LiftMoverMath.Decide(ref pace, k, pass / 60.0, 2.5, out bool write, out _);
                        if (write)
                            writes.Add(pass);
                    }
                    int first = k == 1 ? 1 : math.max(1, k - seed);
                    bool ok = writes.Count > 0 && writes[0] == first;
                    for (int i = 1; i < writes.Count && ok; i++)
                        ok = writes[i] - writes[i - 1] == k;
                    Check(ok, W, $"stride {k}, loop {loopIndex} (seed {seed}): first write {(writes.Count > 0 ? writes[0] : -1)} " +
                                 $"(want {first}), {writes.Count} writes, not every {k} passes");
                }
            }
        }

        // ── MP-5 ────────────────────────────────────────────────────────────────────────────────────────────────────────────
        private static void Mp5CatchUp()
        {
            const string W = "loop-pace/MP-5";
            var settings = LiftMoverMath.PaceSettings.Default;
            var rng = new System.Random(0x5CA1E005);
            const double length = 2400.0;
            const int chairs = 12;
            var offsets = new double[chairs];
            for (int i = 0; i < chairs; i++)
                offsets[i] = i * length / chairs;
            var shown = new double[chairs];

            int nearNotExact = 0, lagTooLong = 0, farStepTooBig = 0, nearPasses = 0, farPasses = 0;
            double worstFarRatio = 0.0;
            for (int loop = 0; loop < 3; loop++)
            {
                double speed = s_Speeds[loop];
                double timeS = 1000.0 + loop * 77.7;
                LiftMoverMath.LoopPace pace = LiftMoverMath.Seed(loop, settings.MaxStride, timeS);
                for (int i = 0; i < chairs; i++)
                    shown[i] = LiftMoverMath.LoopPosition(offsets[i], speed, timeS, length);   // the rebuild pass wrote them
                int lastWritePass = 0;
                double spellDt = 1.0 / 60.0;
                float spellDistance = 100f;
                int spellLeft = 0;
                int spellStartPass = 1;
                for (int pass = 1; pass <= 20000; pass++)
                {
                    if (spellLeft-- <= 0)
                    {
                        spellLeft = 20 + rng.Next(400);
                        spellDt = (1.0 / (20 + rng.Next(125))) * (1 + rng.Next(3));     // 20-144 fps, 1x-3x
                        int band = rng.Next(4);
                        spellDistance = band == 0 ? (float)(rng.NextDouble() * 499.0)    // near
                            : band == 1 ? 500f + (float)(rng.NextDouble() * 500.0)
                            : band == 2 ? 1000f + (float)(rng.NextDouble() * 2000.0)
                            : 3000f + (float)(rng.NextDouble() * 5000.0);
                        spellStartPass = pass;
                    }
                    timeS += spellDt;
                    int stride = LiftMoverMath.StrideFor(spellDistance, speed, spellDt, settings);
                    LiftMoverMath.Decide(ref pace, stride, timeS, speed, out bool write, out _);
                    if (write)
                    {
                        for (int i = 0; i < chairs; i++)
                            shown[i] = LiftMoverMath.LoopPosition(offsets[i], speed, timeS, length);
                        lastWritePass = pass;
                    }
                    if (pass - lastWritePass > settings.MaxStride - 1)
                        lagTooLong++;
                    if (stride == 1)
                    {
                        nearPasses++;
                        for (int i = 0; i < chairs; i++)
                        {
                            double exact = LiftMoverMath.LoopPosition(offsets[i], speed, timeS, length);
                            if (!write || BitConverter.DoubleToInt64Bits(exact) != BitConverter.DoubleToInt64Bits(shown[i]))
                            {
                                nearNotExact++;
                                break;
                            }
                        }
                    }
                    else if (lastWritePass >= spellStartPass)
                    {
                        // Inside a spell of constant dt and distance (the bound is per stride decision; the first pass of a
                        // spell can still show a pose written under the previous spell's stride).
                        farPasses++;
                        double exact = LiftMoverMath.LoopPosition(offsets[0], speed, timeS, length);
                        double diff = math.abs(exact - shown[0]);
                        diff = math.min(diff, length - diff);
                        double allowed = spellDistance * settings.MaxStepRadians;
                        double ratio = diff / allowed;
                        if (ratio > worstFarRatio)
                            worstFarRatio = ratio;
                        if (diff > allowed * (1.0 + 1e-9) && pass - lastWritePass < stride)
                            farStepTooBig++;
                    }
                }
            }
            Check(nearPasses > 1000 && farPasses > 1000, W, $"coverage: {nearPasses} near / {farPasses} far passes");
            Check(nearNotExact == 0, W, $"{nearNotExact} near pass(es) not written at the exact unthrottled pose");
            Check(lagTooLong == 0, W, $"{lagTooLong} pass(es) show a pose older than MaxStride − 1 passes");
            Check(farStepTooBig == 0, W, $"{farStepTooBig} far pass(es) lag the exact pose by more than 0.5 mrad × distance " +
                                          $"(worst ratio {F(worstFarRatio)})");
            Console.WriteLine($"INFO  {W}: {nearPasses} near / {farPasses} far passes; worst far lag {worstFarRatio:F3} of the 0.5 mrad step");
        }

        // ── MP-6 ────────────────────────────────────────────────────────────────────────────────────────────────────────────
        private static void Mp6Bounds()
        {
            const string W = "loop-pace/MP-6";
            const double dt = 1.0 / 60.0;
            foreach (double speed in s_Speeds)
            {
                // Near: tags at ≥ 2 m; staleness < 2 m + one pass of travel.
                LiftMoverMath.LoopPace pace = LiftMoverMath.Seed(0, 8, 0.0);
                double t = 0.0, lastTag = 0.0;
                int badGap = 0, stale = 0, tags = 0;
                for (int pass = 1; pass <= 6000; pass++)
                {
                    t += dt;
                    LiftMoverMath.Decide(ref pace, 1, t, speed, out bool write, out bool tag);
                    if (tag)
                    {
                        tags++;
                        if ((t - lastTag) * speed < LiftMoverMath.BoundsRefreshMeters)
                            badGap++;
                        lastTag = t;
                    }
                    if ((t - lastTag) * speed >= LiftMoverMath.BoundsRefreshMeters + speed * dt + 1e-9)
                        stale++;
                }
                Check(tags > 0 && badGap == 0 && stale == 0, W,
                      $"near {speed} m/s: {tags} tags, {badGap} under 2 m, {stale} pass(es) ≥ 2 m + one pass stale");

                // Far at stride 8: tags only on writes, at ≥ 16 m.
                pace = LiftMoverMath.Seed(1, 8, 0.0);
                t = 0.0;
                lastTag = 0.0;
                int tagNoWrite = 0, farGap = 0;
                tags = 0;
                for (int pass = 1; pass <= 6000; pass++)
                {
                    t += dt;
                    LiftMoverMath.Decide(ref pace, 8, t, speed, out bool write, out bool tag);
                    if (tag && !write)
                        tagNoWrite++;
                    if (tag)
                    {
                        tags++;
                        if ((t - lastTag) * speed < 8 * LiftMoverMath.BoundsRefreshMeters)
                            farGap++;
                        lastTag = t;
                    }
                }
                Check(tags > 0 && tagNoWrite == 0 && farGap == 0, W,
                      $"far {speed} m/s stride 8: {tags} tags, {tagNoWrite} without a write, {farGap} under 16 m");

                // Return: far spell with 10 m of untagged travel, then the first near pass tags.
                pace = LiftMoverMath.Seed(2, 8, 0.0);
                t = 0.0;
                bool sawFarTag = false;
                for (int pass = 1; pass <= 600; pass++)
                {
                    t += dt;
                    LiftMoverMath.Decide(ref pace, 8, t, speed, out _, out bool tag);
                    sawFarTag |= tag;
                }
                double untagged = (t - pace.LastBoundsTimeS) * speed;
                t += dt;
                LiftMoverMath.Decide(ref pace, 1, t, speed, out bool backWrite, out bool backTag);
                Check(backWrite, W, $"{speed} m/s: the pass back near writes");
                Check(untagged + speed * dt < LiftMoverMath.BoundsRefreshMeters || backTag, W,
                      $"{speed} m/s: back near with {F(untagged)} m untagged travel — no bounds tag");
            }
        }

        // ── MP-7 ────────────────────────────────────────────────────────────────────────────────────────────────────────────
        private static void Mp7TagRate()
        {
            const string W = "loop-pace/MP-7";
            const double dt = 1.0 / 60.0;
            const double speed = 2.5;
            LiftMoverMath.LoopPace near = LiftMoverMath.Seed(0, 8, 0.0), far = LiftMoverMath.Seed(1, 8, 0.0);
            int nearTags = 0, farTags = 0;
            double t = 0.0;
            for (int pass = 1; pass <= 60 * 60; pass++)   // one minute
            {
                t += dt;
                LiftMoverMath.Decide(ref near, 1, t, speed, out _, out bool a);
                LiftMoverMath.Decide(ref far, 8, t, speed, out _, out bool b);
                if (a) nearTags++;
                if (b) farTags++;
            }
            double nearRate = nearTags / 60.0, farRate = farTags / 60.0;
            // 2 m of travel takes 48 passes of 2.5 / 60 m on paper; float accumulation makes it 48-49: 1.22-1.25 per second.
            Check(nearRate >= 60.0 / 50.0 && nearRate <= 1.25 + 1e-9, W, $"near fixed-grip: {nearRate:F3} tags/s per chair (want 1.20-1.25)");
            Check(farRate <= nearRate / 8.0 + 0.02, W, $"far stride 8: {farRate:F3} tags/s per chair (want ≤ {nearRate / 8.0:F3})");
            Console.WriteLine($"INFO  {W}: near {nearRate:F3} / far {farRate:F3} bounds tags per second per chair (fixed-grip, 60 fps, 1x)");
        }

        // ── MP-8 ────────────────────────────────────────────────────────────────────────────────────────────────────────────
        private static void Mp8Percentiles()
        {
            const string W = "loop-pace/MP-8";
            var scratch = new List<float>();
            var values = new float[100];
            for (int i = 0; i < 100; i++)
                values[i] = 100 - i;    // 100..1, unsorted
            LiftMoverMath.Percentiles(values, 100, scratch, out float p50, out float p95, out float max);
            Check(p50 == 50f && p95 == 95f && max == 100f, W, $"1..100: p50 {p50} p95 {p95} max {max} (want 50 / 95 / 100)");
            LiftMoverMath.Percentiles(values, 0, scratch, out p50, out p95, out max);
            Check(p50 == 0f && p95 == 0f && max == 0f, W, "empty sample → 0");
            LiftMoverMath.Percentiles(new[] { 3f }, 1, scratch, out p50, out p95, out max);
            Check(p50 == 3f && p95 == 3f && max == 3f, W, "one sample → itself");
            Check(LiftMoverMath.Rank(0.95, 600) == 569 && LiftMoverMath.Rank(1.0, 600) == 599 && LiftMoverMath.Rank(0.0, 600) == 0,
                  W, "rank bounds");
        }

        // ── MP-9 ────────────────────────────────────────────────────────────────────────────────────────────────────────────
        private static void Mp9Owners()
        {
            const string W = "loop-pace/MP-9";
            var edges = new List<LiftLaneScope.LineEdge<int>>
            {
                new LiftLaneScope.LineEdge<int> { Edge = 10, Start = 1, End = 2 },
                new LiftLaneScope.LineEdge<int> { Edge = 11, Start = 2, End = 3 },   // shares node 2
                new LiftLaneScope.LineEdge<int> { Edge = 12, Start = 3, End = 0 },   // a null end
            };
            var into = new List<int> { 99 };
            int added = LiftLaneScope.OwnersOf(edges, into, new HashSet<int>());
            string got = string.Join(",", into);
            Check(added == 6 && got == "99,10,1,2,11,3,12", W, $"owners {got} (added {added}); want 99,10,1,2,11,3,12 (6 added)");
        }

        // ── MP-10 ───────────────────────────────────────────────────────────────────────────────────────────────────────────
        private sealed class FakeLane
        {
            public int Id, Owner;
            public bool Hv, Live = true;
        }

        private static void Mp10Harvest()
        {
            const string W = "loop-pace/MP-10";
            // Owners: lift line edges 100-104 and nodes 1-6 (prefab "lift"); a vanilla HV line edge 200 / node 20; a water edge 300.
            var liftOwners = new HashSet<int> { 100, 101, 102, 103, 104, 1, 2, 3, 4, 5, 6 };
            var lanes = new List<FakeLane>();
            var subLanes = new Dictionary<int, List<int>>();
            int nextLane = 1000;
            void AddLane(int owner, bool hv, bool live = true)
            {
                var lane = new FakeLane { Id = nextLane++, Owner = owner, Hv = hv, Live = live };
                lanes.Add(lane);
                if (!subLanes.TryGetValue(owner, out var list))
                    subLanes[owner] = list = new List<int>();
                list.Add(lane.Id);
            }
            for (int e = 100; e <= 104; e++)
            {
                AddLane(e, hv: true);
                AddLane(e, hv: true);
                AddLane(e, hv: false);   // the LV comm lane
            }
            for (int node = 1; node <= 6; node++)
                AddLane(node, hv: true);
            AddLane(200, hv: true);      // vanilla HV line
            AddLane(20, hv: true);
            AddLane(300, hv: false);     // water
            AddLane(102, hv: true, live: false);   // Deleted / Temp lane on a lift edge

            var byId = new Dictionary<int, FakeLane>();
            foreach (FakeLane l in lanes)
                byId[l.Id] = l;
            bool Keep(FakeLane l) => l.Live && l.Hv && liftOwners.Contains(l.Owner);   // the full scan's filter

            var full = new List<int>();
            foreach (FakeLane l in lanes)
                if (Keep(l))
                    full.Add(l.Id);

            var census = new List<LiftLaneScope.LineEdge<int>>
            {
                new LiftLaneScope.LineEdge<int> { Edge = 100, Start = 1, End = 2 },
                new LiftLaneScope.LineEdge<int> { Edge = 101, Start = 2, End = 3 },
                new LiftLaneScope.LineEdge<int> { Edge = 102, Start = 3, End = 4 },
                new LiftLaneScope.LineEdge<int> { Edge = 103, Start = 4, End = 5 },
                new LiftLaneScope.LineEdge<int> { Edge = 104, Start = 5, End = 6 },
            };
            List<int> Scoped(List<LiftLaneScope.LineEdge<int>> edgesOfCensus, Func<FakeLane, bool> filter)
            {
                var owners = new List<int>();
                LiftLaneScope.OwnersOf(edgesOfCensus, owners, new HashSet<int>());
                var seen = new HashSet<int>();
                var kept = new List<int>();
                foreach (int o in owners)
                {
                    if (!subLanes.TryGetValue(o, out var list))
                        continue;
                    foreach (int id in list)
                        if (seen.Add(id) && filter(byId[id]))
                            kept.Add(id);
                }
                return kept;
            }

            var missed = new List<int>();
            var extra = new List<int>();
            List<int> scoped = Scoped(census, Keep);
            LiftLaneScope.CompareHarvests(full, scoped, missed, extra);
            Check(missed.Count == 0 && extra.Count == 0 && scoped.Count == full.Count && full.Count == 16, W,
                  $"identical sets expected: full {full.Count} (want 16), scoped {scoped.Count}, missed {missed.Count}, extra {extra.Count}");

            // A lift node no census edge touches (an orphan tower node): the watchdog reports its lane missed.
            liftOwners.Add(7);
            AddLane(7, hv: true);
            byId[nextLane - 1] = lanes[lanes.Count - 1];
            full.Add(nextLane - 1);
            scoped = Scoped(census, Keep);
            LiftLaneScope.CompareHarvests(full, scoped, missed, extra);
            Check(missed.Count == 1 && missed[0] == nextLane - 1 && extra.Count == 0, W,
                  $"orphan lift node lane: missed {string.Join(",", missed)} extra {extra.Count} (want missed {nextLane - 1})");

            // A drifted scoped filter (keeps the dead lane): reported extra.
            scoped = Scoped(census, l => l.Hv && liftOwners.Contains(l.Owner));
            LiftLaneScope.CompareHarvests(full, scoped, missed, extra);
            Check(extra.Count == 1 && !byId[extra[0]].Live, W, $"drifted filter: extra {extra.Count} (want the 1 dead lane)");
        }
    }
}
