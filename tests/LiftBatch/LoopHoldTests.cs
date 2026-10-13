// LoopHoldTests — Phase D (docs/research/art/assets-2026-10/code-round-4/round-4-contract.md §7.1, rules group, with
// round-4-contract-skeptic.md S-10): the pure arithmetic of a chairlift loop that stops and resumes (LiftLoopHold.cs).
// Where-tags `loop-hold/…`. The chair clock is ChairPinSystem's: s = (offset + speed × TimeS) % L (ChairPinSystem.cs:1072-1076,
// :1140-1143), modelled here with the same double expressions.
//   LH-1  Rebase, 10,000 seeded cases with frozen ∈ [0, 2e5] s and 10,000 with frozen ∈ [0, 7.16e7] s (the whole uint frame
//         clock / 60, S-10); offset ∈ [0, L), L ∈ [50, 5,000] m, speed ∈ {2.5, 5.0, 5.5, 10.0}, now ∈ [frozen, frozen + 1e5]:
//         the result is in [0, L) and the circular distance between (r + speed × now) % L and (offset + speed × frozen) % L
//         is < 1e-6 m (MoveChair's idempotence test is 1 mm, ChairPinSystem.cs:1169-1171). Aggregated: one check per
//         property per set; the worst error is printed as an INFO line.
//   LH-2  Rebase(o, v, t, t, L) == o bit for bit for o ∈ [0, L); FrozenPhase(v, t) == v * t bit for bit (PhaseOf's
//         product) and the held pose (o + FrozenPhase) % L == the running pose at t; invalid inputs return the offset.
//   LH-3  hold then resume on a simulated loop of 40 chairs on the grid (offset i × L / 40) for each lift speed: positions
//         at the hold == positions at the resume (< 1e-6 m); spacing mod L unchanged (< 1e-9 m); one frame (1/60 s) after
//         the resume every chair has advanced by speed / 60 (< 1e-6 m).
//   LH-4  GateOf, 2 × 2 × 3 × 2 = 24 rows: Hold only for (row known, Complete, NoPower) and (row known, Complete, closed).

using System;
using System.Collections.Generic;
using System.Globalization;
using SkiResortMod.Systems;

namespace LiftBatch
{
    public static class LoopHoldTests
    {
        private static Func<bool, string, string, bool> s_Check;

        private static readonly double[] s_Speeds = { 2.5, 5.0, 5.5, 10.0 };   // the four types' (double)SpeedMps

        public static void Run(Func<bool, string, string, bool> check)
        {
            s_Check = check;
            Lh1Rebase("loop-hold/LH-1", 0x5EED0D01, 2e5);
            Lh1Rebase("loop-hold/LH-1 uint clock", 0x5EED0D02, 7.16e7);
            Lh2Identity();
            Lh3HoldResume();
            Lh4GateOf();
            s_Check = null;
        }

        private static bool Check(bool cond, string where, string msg) => s_Check(cond, where, msg);

        private static void Eq<T>(T expected, T actual, string where, string what)
        {
            Check(EqualityComparer<T>.Default.Equals(expected, actual), where, what + ": expected " + expected + ", got " + actual);
        }

        private static string R(double x) => x.ToString("R", CultureInfo.InvariantCulture);

        /// <summary>ChairPin's place on the loop: (offset + speed × timeS) % L.</summary>
        private static double PlaceAt(double offset, double speed, double timeS, double length)
        {
            return (offset + LiftLoopHold.FrozenPhase(speed, timeS)) % length;
        }

        /// <summary>Distance between two places on a loop of length L (both in [0, L) or slightly outside by rounding).</summary>
        private static double Circular(double a, double b, double length)
        {
            double d = Math.Abs(a - b) % length;
            return Math.Min(d, length - d);
        }

        private static void Lh1Rebase(string w, int seed, double frozenMax)
        {
            var rng = new Random(seed);
            const int cases = 10000;
            int outOfRange = 0, tooFar = 0;
            double worst = 0.0;
            string worstCase = "";
            for (int c = 0; c < cases; c++)
            {
                double length = 50.0 + rng.NextDouble() * 4950.0;
                double offset = rng.NextDouble() * length;
                if (offset >= length)
                    offset = 0.0;
                double speed = s_Speeds[rng.Next(s_Speeds.Length)];
                double frozen = rng.NextDouble() * frozenMax;
                double now = frozen + rng.NextDouble() * 1e5;
                double r = LiftLoopHold.Rebase(offset, speed, frozen, now, length);
                if (!(r >= 0.0 && r < length))
                    outOfRange++;
                double err = Circular(PlaceAt(r, speed, now, length), PlaceAt(offset, speed, frozen, length), length);
                if (!(err < 1e-6))
                    tooFar++;
                if (err > worst || double.IsNaN(err))
                {
                    worst = err;
                    worstCase = "offset " + R(offset) + ", v " + R(speed) + ", frozen " + R(frozen) + ", now " + R(now) + ", L " + R(length);
                }
            }
            Eq(0, outOfRange, w, "rebased offsets outside [0, L) of " + cases);
            Eq(0, tooFar, w, "cases with the resumed place ≥ 1e-6 m from the frozen place of " + cases + " (worst " + R(worst) + " m: " + worstCase + ")");
            Console.WriteLine("INFO  " + w + ": worst circular error " + R(worst) + " m over " + cases + " cases (frozen ≤ " + R(frozenMax) + " s)");
        }

        private static void Lh2Identity()
        {
            const string W = "loop-hold/LH-2";
            var rng = new Random(0x5EED0D03);
            int notSame = 0, phaseDiff = 0, poseDiff = 0;
            for (int c = 0; c < 2000; c++)
            {
                double length = 50.0 + rng.NextDouble() * 4950.0;
                double o = c == 0 ? 0.0 : c == 1 ? length - length * 1e-15 : rng.NextDouble() * length;
                if (o >= length)
                    o = 0.0;
                double v = s_Speeds[c % s_Speeds.Length];
                double t = rng.NextDouble() * 7.16e7;
                if (BitConverter.DoubleToInt64Bits(LiftLoopHold.Rebase(o, v, t, t, length)) != BitConverter.DoubleToInt64Bits(o))
                    notSame++;
                if (BitConverter.DoubleToInt64Bits(LiftLoopHold.FrozenPhase(v, t)) != BitConverter.DoubleToInt64Bits(v * t))
                    phaseDiff++;
                // the hold-entry pose is the running pose of the same frame (ChairPinSystem.cs:1072-1076 with phase = v × t)
                double running = (o + v * t) % length;
                if (BitConverter.DoubleToInt64Bits(PlaceAt(o, v, t, length)) != BitConverter.DoubleToInt64Bits(running))
                    poseDiff++;
            }
            Eq(0, notSame, W, "Rebase(o, v, t, t, L) not bit-identical to o (of 2,000)");
            Eq(0, phaseDiff, W, "FrozenPhase(v, t) not bit-identical to v * t (of 2,000)");
            Eq(0, poseDiff, W, "held pose not bit-identical to the running pose at the frozen clock (of 2,000)");

            // invalid inputs return the offset unchanged
            Eq(12.5, LiftLoopHold.Rebase(12.5, 2.5, 100.0, 200.0, 0.0), W, "L 0");
            Eq(12.5, LiftLoopHold.Rebase(12.5, 2.5, 100.0, 200.0, -5.0), W, "L -5");
            Eq(12.5, LiftLoopHold.Rebase(12.5, 2.5, 100.0, 200.0, double.NaN), W, "L NaN");
            Eq(12.5, LiftLoopHold.Rebase(12.5, 2.5, 100.0, 200.0, double.PositiveInfinity), W, "L +inf");
            Eq(12.5, LiftLoopHold.Rebase(12.5, double.NaN, 100.0, 200.0, 2000.0), W, "speed NaN");
            Eq(12.5, LiftLoopHold.Rebase(12.5, 2.5, double.PositiveInfinity, 200.0, 2000.0), W, "frozen +inf");
            Eq(12.5, LiftLoopHold.Rebase(12.5, 2.5, 100.0, double.NaN, 2000.0), W, "now NaN");
            Check(double.IsNaN(LiftLoopHold.Rebase(double.NaN, 2.5, 100.0, 200.0, 2000.0)), W, "offset NaN comes back NaN (unchanged)");
            // a resume before the hold (a clock that went back) still lands in [0, L)
            double back = LiftLoopHold.Rebase(10.0, 2.5, 200.0, 100.0, 2000.0);
            Check(back >= 0.0 && back < 2000.0 && Math.Abs(back - 260.0) < 1e-9, W, "now < frozen: 10 + 2.5 × 100 = 260 (got " + R(back) + ")");
            // a negative shift wraps once into [0, L)
            double wrap = LiftLoopHold.Rebase(10.0, 2.5, 100.0, 200.0, 2000.0);
            Check(Math.Abs(wrap - 1760.0) < 1e-9, W, "10 − 250 wraps to 1,760 (got " + R(wrap) + ")");
        }

        private static void Lh3HoldResume()
        {
            const string W = "loop-hold/LH-3";
            const int n = 40;
            const double length = 2000.0, frozen = 100000.123, now = frozen + 3600.5, dt = 1.0 / 60.0;
            foreach (double v in s_Speeds)
            {
                var offsets = new double[n];
                var rebased = new double[n];
                for (int i = 0; i < n; i++)
                {
                    offsets[i] = i * length / n;
                    rebased[i] = LiftLoopHold.Rebase(offsets[i], v, frozen, now, length);
                }
                double worstPlace = 0.0, worstSpacing = 0.0, worstStep = 0.0;
                for (int i = 0; i < n; i++)
                {
                    double held = PlaceAt(offsets[i], v, frozen, length);
                    double resumed = PlaceAt(rebased[i], v, now, length);
                    worstPlace = Math.Max(worstPlace, Circular(held, resumed, length));
                    int j = (i + 1) % n;
                    double before = ((offsets[j] - offsets[i]) % length + length) % length;
                    double after = ((rebased[j] - rebased[i]) % length + length) % length;
                    worstSpacing = Math.Max(worstSpacing, Circular(before, after, length));
                    double next = PlaceAt(rebased[i], v, now + dt, length);
                    worstStep = Math.Max(worstStep, Circular(next, (held + v * dt) % length, length));
                }
                string tag = " (v " + R(v) + ")";
                Check(worstPlace < 1e-6, W, "held place == resumed place, worst " + R(worstPlace) + " m" + tag);
                Check(worstSpacing < 1e-9, W, "spacing unchanged, worst " + R(worstSpacing) + " m" + tag);
                Check(worstStep < 1e-6, W, "one frame later every chair advanced by v / 60, worst " + R(worstStep) + " m" + tag);
            }
        }

        private static void Lh4GateOf()
        {
            const string W = "loop-hold/LH-4";
            LiftPower[] powers = { LiftPower.Unknown, LiftPower.Powered, LiftPower.NoPower };
            int rows = 0, holds = 0;
            for (int k = 0; k < 2; k++)
                for (int c = 0; c < 2; c++)
                    foreach (LiftPower p in powers)
                        for (int z = 0; z < 2; z++)
                        {
                            bool known = k == 1, complete = c == 1, closed = z == 1;
                            bool hold = known && complete && (p == LiftPower.NoPower || closed);
                            LoopGate want = hold ? LoopGate.Hold : LoopGate.Run;
                            Eq(want, LiftLoopHold.GateOf(known, complete, p, closed), W,
                               "GateOf(known " + known + ", complete " + complete + ", " + p + ", closed " + closed + ")");
                            rows++;
                            if (hold)
                                holds++;
                        }
            Eq(24, rows, W, "row count");
            Eq(4, holds, W, "Hold rows (known + Complete with NoPower, closed, or both)");
            Eq((byte)0, (byte)LoopGate.Run, W, "LoopGate.Run = 0");
            Eq((byte)1, (byte)LoopGate.Hold, W, "LoopGate.Hold = 1");
        }
    }
}
