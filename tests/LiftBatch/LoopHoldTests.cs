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
//   LH-5  T13 (report item 78, LiftClosedRule): closed = the ski area has no open run, with one game hour (HoldFrames 10,923 sim
//         frames) of hysteresis: the first observation is taken at once; the state flips only after the observed state held
//         HoldFrames (boundary exact, both ways, uint-wrap safe); a flicker never flips; an interrupted spell restarts the
//         clock; at the 1,024-frame charge cadence the flip lands in [10,923, 11,947); Not linked is never closed; GateOf
//         stops a Complete powered lift and resumes it (a season walk); not Complete / no row never holds; the tram
//         shuttle's predicate Carries(true, power, closed) follows.

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
            Lh5ClosedRule();
            Lh6ExpectsLoop();
            Lh7HeldSuffix();
            Lh8TramSummer();
            LvIconRule();
            s_Check = null;
        }

        /// <summary>LH-6 (T116, 2026-10-20 log 23:14:34): the tram (census row type = SkiCatalog index of "tram", Shuttle)
        /// owes ChairPin no loop, so CompleteLiftsWithoutLoop skips it; every Loop type and an untyped row still do.</summary>
        private static void Lh6ExpectsLoop()
        {
            const string W = "loop-hold/LH-6";
            int seen = 0;
            for (int i = 0; i < 4; i++)
            {
                LiftTypeDef def = SkiCatalog.LiftByIndex(i);
                if (!Check(def != null, W, "SkiCatalog.LiftByIndex(" + i + ") is a row"))
                    continue;
                seen++;
                bool shuttle = def.Mover == MoverKind.Shuttle;
                Eq(def.Id == "tram", shuttle, W, def.Id + " is the one Shuttle type");
                Eq(!shuttle, LiftLoopHold.ExpectsLoop(def), W, def.Id + " expects a ChairPin loop");
            }
            Eq(4, seen, W, "four lift types");
            Check(LiftLoopHold.ExpectsLoop(null), W, "an untyped row (NoType) keeps the WARN (as before T116)");
            Check(LiftLoopHold.ExpectsLoop(SkiCatalog.LiftByIndex(255)) , W, "census NoType 255 resolves to null -> expects a loop");
            // The log's case: the tram row 427911:31 -> 85587:31 is census-Complete with no ChairPin loop.
            LiftTypeDef tram = null;
            for (int i = 0; i < 4; i++)
                if (SkiCatalog.LiftByIndex(i)?.Id == "tram")
                    tram = SkiCatalog.LiftByIndex(i);
            Check(tram != null && !LiftLoopHold.ExpectsLoop(tram), W,
                  "replay 23:14:34: complete tram 427911:31 -> 85587:31 without a loop is not reported");
        }

        /// <summary>LH-7 (T116): the held count names its reason. Replays the 2026-10-19 FG lift (log 588: "Running → Held
        /// (closed)", 1 loop of 110 chairs) that the old text printed as "1 held (no power)".</summary>
        private static void Lh7HeldSuffix()
        {
            const string W = "loop-hold/LH-7";
            Eq("", LiftLoopHold.HeldSuffix(0, 0), W, "nothing held: no suffix (a running city's line is unchanged)");
            Eq(", 1 held (closed)", LiftLoopHold.HeldSuffix(0, 1), W, "FG 469677:11 replay: one loop held closed");
            Eq(", 110 held (closed)", LiftLoopHold.HeldSuffix(0, 110), W, "FG replay, pass-alive chair count");
            Eq(", 1 held (no power)", LiftLoopHold.HeldSuffix(1, 0), W, "one loop held for power: the old text, unchanged");
            Eq(", 3 held (1 no power, 2 closed)", LiftLoopHold.HeldSuffix(1, 2), W, "mixed reasons");
            Eq("", LiftLoopHold.HeldSuffix(-1, 0), W, "negative counts read as 0");
            Eq(", 2 held (closed)", LiftLoopHold.HeldSuffix(-4, 2), W, "a negative power count is ignored");
        }

        /// <summary>LH-8 (DECISIONS 347 e, Richard 2026-10-20: "trams can run in summer as sightseeing. This is typical."): a Shuttle
        /// type is exempt from the T13 no-open-run hold (never closed, billed full); every Loop type and an untyped row keep it.
        /// Replays the 10-19 tram 427911:31 -> 85587:31: complete, powered, area closed (summer, snow 0) -> it now carries.</summary>
        private static void Lh8TramSummer()
        {
            const string W = "loop-hold/LH-8";
            LiftTypeDef tram = null;
            for (int i = 0; i < 4; i++)
            {
                LiftTypeDef def = SkiCatalog.LiftByIndex(i);
                if (def == null)
                    continue;
                bool shuttle = def.Mover == MoverKind.Shuttle;
                if (shuttle)
                    tram = def;
                Eq(!shuttle, LiftClosedRule.ClosedForLift(true, def), W, def.Id + " held when its area has no open run");
                Eq(false, LiftClosedRule.ClosedForLift(false, def), W, def.Id + " not closed while a run is open");
                Eq(shuttle, LiftClosedRule.AreaOpenForBilling(false, def), W, def.Id + " billed as open with no open run");
                Eq(true, LiftClosedRule.AreaOpenForBilling(true, def), W, def.Id + " billed as open with an open run");
            }
            Check(LiftClosedRule.HoldsWhenClosed(null) && LiftClosedRule.ClosedForLift(true, null), W,
                  "an untyped row keeps the hold (as before)");
            bool closed = LiftClosedRule.ClosedForLift(true, tram);
            Check(tram != null && LiftServedRule.Carries(true, LiftPower.Powered, closed), W,
                  "replay 23:15:16: tram 427911:31 complete, powered, area NoOpenRun -> carries (was Held (closed))");
            LiftTypeDef fg = SkiCatalog.LiftByIndex(0);
            Check(fg != null && fg.Mover == MoverKind.Loop && !LiftServedRule.Carries(true, LiftPower.Powered, LiftClosedRule.ClosedForLift(true, fg)), W,
                  "replay 22:58:19: FG 469677:11 in the same area stays held (closed)");
            Eq(", 1 held (closed)", LiftLoopHold.HeldSuffix(0, 1), W, "the FG's held reason text stays 'closed'");
        }

        /// <summary>LV-1..LV-4 (T117 follow-up, DECISIONS 347 d; StationLvRule header): the vanilla "low voltage not connected"
        /// icon goes when an LV wire reaches the stub, at its node or on its middle; it stays with no wire. Replays 10-19.</summary>
        private static void LvIconRule()
        {
            const string W = "lv-icon";
            Eq(LvStubWire.None, StationLvRule.Classify(0, 0), W + "/LV-1", "no foreign LV edge or node: no wire");
            Eq(LvStubWire.AtNode, StationLvRule.Classify(1, 0), W + "/LV-1", "a wire edge ending at the stub node");
            Eq(LvStubWire.MidStub, StationLvRule.Classify(0, 1), W + "/LV-1", "a wire node local-connected onto the stub's middle");
            Eq(LvStubWire.AtNode, StationLvRule.Classify(1, 1), W + "/LV-1", "both: at node wins (the vanilla check sees it)");
            // LV-2 replay: the FG base 469677:11 (Connected 1224 / 1224, the LV icon still up at 22:57:40 and 23:02:57; its stub lies
            // fully outside the mesh, so the wire end can land mid-stub).
            Check(StationLvRule.ClearsVanillaIcon(true, StationLvRule.Classify(0, 1)), W + "/LV-2",
                  "FG base 469677:11 replay: wire mid-stub + vanilla LV icon -> removed");
            // LV-3 replay: the DQ 395053:19 / gondola 380273:23 bases, never wired (0 / 1222): the icon is the truth and stays.
            Check(!StationLvRule.ClearsVanillaIcon(true, StationLvRule.Classify(0, 0)), W + "/LV-3",
                  "DQ / gondola base replay: no wire -> the icon stays");
            // LV-4: the tram base 427911:31 (stub inside the mesh, wire at the tip node): vanilla already cleared it; nothing to do.
            Check(!StationLvRule.ClearsVanillaIcon(false, StationLvRule.Classify(1, 0)) && StationLvRule.ClearsVanillaIcon(true, LvStubWire.AtNode),
                  W + "/LV-4", "tram base replay: no icon -> no command; a stale icon at a wired node is removed too");
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

        // ── T13 (report item 78): closed = the lift's ski area has no open run, with one game hour of hysteresis ──

        /// <summary>Feeds the rule one observation every <paramref name="stride"/> frames from <paramref name="from"/> to
        /// <paramref name="to"/> inclusive with a constant area state; returns the first frame at which the published state
        /// equals <paramref name="wantClosed"/>, or -1.</summary>
        private static long FirstFlip(ref AreaClosedState s, bool areaOpen, uint from, uint to, uint stride, bool wantClosed)
        {
            for (uint f = from; f <= to; f += stride)
            {
                bool closed = LiftClosedRule.Step(ref s, areaOpen, f);
                if (closed == wantClosed)
                    return f;
            }
            return -1;
        }

        private static void Lh5ClosedRule()
        {
            const string W = "loop-hold/LH-5";

            // The delay: one game hour (262,144 sim frames a day, SkiMoneyRules.TicksPerDay).
            Eq(10923u, LiftClosedRule.HoldFrames, W, "HoldFrames");
            Check(LiftClosedRule.HoldFrames * 24u >= 262144u && (LiftClosedRule.HoldFrames - 1u) * 24u < 262144u, W,
                  "HoldFrames is 262,144 / 24 rounded up (one game hour)");

            // a: the first settled observation is taken at once (no debounce on a load): closed, and open.
            var first = new AreaClosedState();
            Eq(true, LiftClosedRule.Step(ref first, false, 5000u), W, "first observation no-open-run: closed at once");
            var firstOpen = new AreaClosedState();
            Eq(false, LiftClosedRule.Step(ref firstOpen, true, 5000u), W, "first observation open-run: open at once");
            Check(!LiftClosedRule.ClosedFor(true, new AreaClosedState()), W, "an unobserved area is not closed");

            // b: a lift with no ski area is never closed, whatever a state says.
            var closedState = new AreaClosedState { Known = true, Closed = true };
            Check(!LiftClosedRule.ClosedFor(false, closedState), W, "Not linked: never closed, even beside a closed state");
            Check(LiftClosedRule.ClosedFor(true, closedState), W, "linked, area closed: closed");
            Check(!LiftClosedRule.ClosedFor(true, new AreaClosedState { Known = true, Closed = false }), W, "linked, area open: open");

            // c: open to closed only after the no-open-run state has HELD HoldFrames (boundary exact).
            var s = new AreaClosedState();
            LiftClosedRule.Step(ref s, true, 1000u);
            Eq(false, LiftClosedRule.Step(ref s, false, 2000u), W, "no open run seen at 2000: still open (the clock starts)");
            Eq(false, LiftClosedRule.Step(ref s, false, 2000u + LiftClosedRule.HoldFrames - 1u), W, "HoldFrames - 1 later: still open");
            Eq(true, LiftClosedRule.Step(ref s, false, 2000u + LiftClosedRule.HoldFrames), W, "HoldFrames later: closed");
            Eq(true, LiftClosedRule.Step(ref s, false, 2000u + LiftClosedRule.HoldFrames + 5000u), W, "stays closed");

            // d: reopen: closed to open only after an open run has held HoldFrames.
            Eq(true, LiftClosedRule.Step(ref s, true, 100000u), W, "a run opens: still closed (the clock starts)");
            Eq(true, LiftClosedRule.Step(ref s, true, 100000u + LiftClosedRule.HoldFrames - 1u), W, "HoldFrames - 1 later: still closed");
            Eq(false, LiftClosedRule.Step(ref s, true, 100000u + LiftClosedRule.HoldFrames), W, "HoldFrames later: open again");

            // e: at the charge cadence (an observation every 1,024 frames) the flip lands in [HoldFrames, HoldFrames + 1,024).
            var cad = new AreaClosedState();
            LiftClosedRule.Step(ref cad, true, 0u);
            long flip = FirstFlip(ref cad, false, 1024u, 1024u + 40000u, 1024u, true);
            Check(flip >= 1024 + LiftClosedRule.HoldFrames && flip < 1024 + LiftClosedRule.HoldFrames + 1024, W,
                  "cadence 1,024: closes " + (flip - 1024) + " frames after the first sighting (>= 10,923, < 11,947)");
            long back = FirstFlip(ref cad, true, 60000u, 60000u + 40000u, 1024u, false);
            Check(back >= 60000 + LiftClosedRule.HoldFrames && back < 60000 + LiftClosedRule.HoldFrames + 1024, W,
                  "cadence 1,024: reopens " + (back - 60000) + " frames after the run appears (>= 10,923, < 11,947)");

            // f: a flicker shorter than the hold never flips (an area that thaws and refreezes).
            var fl = new AreaClosedState();
            LiftClosedRule.Step(ref fl, true, 0u);
            int flips = 0;
            bool last = false;
            for (uint f = 1024u; f < 200000u; f += 1024u)
            {
                bool open = (f / 8192u) % 2u == 0u;   // 8,192 frames open, 8,192 closed: each spell < HoldFrames
                bool now = LiftClosedRule.Step(ref fl, open, f);
                if (now != last)
                    flips++;
                last = now;
            }
            Eq(0, flips, W, "alternating 8,192-frame spells: the published state never flips");

            // g: an interrupted spell restarts the clock (no accumulation: 8,000 + 8,000 frames of closed is not one hour).
            var rs = new AreaClosedState();
            LiftClosedRule.Step(ref rs, true, 0u);
            LiftClosedRule.Step(ref rs, false, 1000u);
            LiftClosedRule.Step(ref rs, false, 9000u);
            LiftClosedRule.Step(ref rs, true, 9500u);                 // open again: pending cleared
            LiftClosedRule.Step(ref rs, false, 10000u);               // a new spell starts here
            Eq(false, LiftClosedRule.Step(ref rs, false, 10000u + LiftClosedRule.HoldFrames - 1u), W, "restarted spell: not yet");
            Eq(true, LiftClosedRule.Step(ref rs, false, 10000u + LiftClosedRule.HoldFrames), W, "restarted spell: closes HoldFrames after ITS start");

            // h: wrap-safe across the uint frame counter.
            var wr = new AreaClosedState();
            LiftClosedRule.Step(ref wr, true, 0xFFFFF000u);
            LiftClosedRule.Step(ref wr, false, 0xFFFFF800u);
            Eq(false, LiftClosedRule.Step(ref wr, false, unchecked(0xFFFFF800u + LiftClosedRule.HoldFrames - 1u)), W, "across the wrap: not yet");
            Eq(true, LiftClosedRule.Step(ref wr, false, unchecked(0xFFFFF800u + LiftClosedRule.HoldFrames)), W, "across the wrap: closed");

            // i: GateOf: closed stops a Complete powered lift; it reopens when the area does; not Complete / no row never holds.
            foreach (LiftPower p in new[] { LiftPower.Unknown, LiftPower.Powered })
            {
                Eq(LoopGate.Run, LiftLoopHold.GateOf(true, true, p, false), W, "Complete, " + p + ", area open: Run");
                Eq(LoopGate.Hold, LiftLoopHold.GateOf(true, true, p, true), W, "Complete, " + p + ", area closed: Hold");
                Eq(LoopGate.Run, LiftLoopHold.GateOf(true, true, p, false), W, "Complete, " + p + ", a run opens: Run again");
                Eq(LoopGate.Run, LiftLoopHold.GateOf(true, false, p, true), W, "not Complete, closed: Run (fail open)");
                Eq(LoopGate.Run, LiftLoopHold.GateOf(false, true, p, true), W, "no census row, closed: Run (fail open)");
            }

            // j: the whole chain over a season: area state, hysteresis, closed flag, gate; a powered Complete linked lift.
            var sum = new AreaClosedState();
            LoopGate g0 = Gate(ref sum, true, true, 0u);                       // autumn: a run is open
            LoopGate g1 = Gate(ref sum, false, true, 100000u);                 // the last run closes
            LoopGate g2 = Gate(ref sum, false, true, 100000u + LiftClosedRule.HoldFrames - 1u);
            LoopGate g3 = Gate(ref sum, false, true, 100000u + LiftClosedRule.HoldFrames);
            LoopGate g4 = Gate(ref sum, true, true, 400000u);                  // a run is painted open
            LoopGate g5 = Gate(ref sum, true, true, 400000u + LiftClosedRule.HoldFrames);
            Eq(LoopGate.Run, g0, W, "season: run open: Run");
            Eq(LoopGate.Run, g1, W, "season: last run just closed: still Run (hysteresis)");
            Eq(LoopGate.Run, g2, W, "season: HoldFrames - 1: still Run");
            Eq(LoopGate.Hold, g3, W, "season: HoldFrames: Hold (chairs stop)");
            Eq(LoopGate.Hold, g4, W, "season: run opened: still Hold (hysteresis)");
            Eq(LoopGate.Run, g5, W, "season: HoldFrames later: Run (chairs resume)");
            var nl = new AreaClosedState();
            LiftClosedRule.Step(ref nl, false, 0u);
            Eq(LoopGate.Run, GateOfLinked(false, nl), W, "Not linked lift beside a closed state: Run (never closed)");

            // k: the tram shuttle predicate (TramShuttleSystem.Gate: Carries(true, power, IsClosed)): Running, Held, Running.
            Eq(true, LiftServedRule.Carries(true, LiftPower.Powered, false), W, "tram: powered, area open: carries (Running)");
            Eq(false, LiftServedRule.Carries(true, LiftPower.Powered, true), W, "tram: powered, area closed: held");
            Eq(false, LiftServedRule.Carries(true, LiftPower.Unknown, true), W, "tram: power unknown, area closed: held");
            Eq(true, LiftServedRule.Carries(true, LiftPower.Powered, false), W, "tram: a run opens: carries again");
            Eq(false, LiftServedRule.Carries(true, LiftPower.NoPower, false), W, "tram: no power still holds (power is read first)");
        }

        private static LoopGate Gate(ref AreaClosedState s, bool areaOpen, bool linked, uint frame)
        {
            LiftClosedRule.Step(ref s, areaOpen, frame);
            return GateOfLinked(linked, s);
        }

        private static LoopGate GateOfLinked(bool linked, in AreaClosedState s)
        {
            return LiftLoopHold.GateOf(true, true, LiftPower.Powered, LiftClosedRule.ClosedFor(linked, s));
        }
    }
}
