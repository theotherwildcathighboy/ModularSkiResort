// Tests for LiftServedRule.Carries (the run predicate) and LiftServedRule.GuardPower (the flicker guard), alone and composed
// with the power rule PowerOf (4 arguments). Where-tags `power-gate/...`.
//   PG-1  Carries over all 12 rows (complete x Unknown / Powered / NoPower x closed): oracle complete && p != NoPower &&
//         !closed; equal to the bool overload with shownPowered = p != NoPower, and to Lift.Serves for a lift with a chord and
//         a finite TopY.
//   PG-2  GuardPower, frame-stepped through Reader (a model of LiftCensusSystem.ReadLiftPower: PowerOf with the last effective
//         value, then GuardPower; the first read of a station has no history and is not guarded); reads every 64 frames unless
//         said. Cases: steady power, a cut and the inclusive 512-frame boundary, a brownout (raw NoPower 128 / Powered 256,
//         20 cycles) read at several phases, streak start, uint wrap, raw Unknown, lastEffective Powered or Unknown, one
//         128-frame NoPower window (the identity holds per consumer-state path, not per lift), the first read of a load
//         (fail open).
//   PG-3  composition with the power rule's three-quarter and half rule (wanted 1,500).
//   PG-4  ExpireStreak: no streak stays none; a gap <= PowerStreakStaleFrames (1,024) keeps the streak, 1,025 drops it;
//         wrap-safe; the constants 512 / uint.MaxValue / 1,024.

using System;
using System.Collections.Generic;
using SkiResortMod.Systems;
using Unity.Mathematics;

namespace LiftBatch
{
    public static class PowerGateTests
    {
        private static Func<bool, string, string, bool> s_Check;

        private const byte Warn = LiftServedRule.FlagNoElectricityWarning;
        private const int Wanted = 1500; // the fixed-grip base (SkiBaseStationBuilding.Prefab:23)

        public static void Run(Func<bool, string, string, bool> check)
        {
            s_Check = check;
            Pg0Constants();
            Pg1Carries();
            Pg2aSteady();
            Pg2bCutAndBack();
            Pg2cBrownout();
            Pg2dSlowReader();
            Pg2eWrap();
            Pg2fUnknown();
            Pg2gNotFromNoPower();
            Pg2hOneDispatchWindow();
            Pg2iFirstRead();
            Pg3F2Composition();
            Pg4ExpireStreak();
            s_Check = null;
        }

        private static bool Check(bool cond, string where, string msg) => s_Check(cond, where, msg);

        private static void Eq<T>(T expected, T actual, string where, string what)
        {
            Check(EqualityComparer<T>.Default.Equals(expected, actual), where, what + ": expected " + expected + ", got " + actual);
        }

        /// <summary>The consumer the census reads: flags, wanted, fulfilled (ElectricityConsumer.cs:8-12).</summary>
        private struct Consumer
        {
            public byte Flags;
            public int Fulfilled;
            public static Consumer Fed => new Consumer { Flags = 0, Fulfilled = Wanted };
            public static Consumer Cut => new Consumer { Flags = Warn, Fulfilled = 0 };
            public static Consumer At(byte flags, int fulfilled) => new Consumer { Flags = flags, Fulfilled = fulfilled };
        }

        /// <summary>A model of the census's one reader (LiftCensusSystem.ReadLiftPower): raw = PowerOf(flags, wanted, fulfilled,
        /// known ? last : Unknown); power = known ? GuardPower(raw, last, ...) : raw; last = power.</summary>
        private sealed class Reader
        {
            public bool Known;
            public LiftPower Last = LiftPower.Unknown;
            public uint BackSince = LiftServedRule.NoBackSince;
            public int Recuts;

            public LiftPower Read(Consumer c, uint frame)
            {
                LiftPower raw = LiftServedRule.PowerOf(c.Flags, Wanted, c.Fulfilled, Known ? Last : LiftPower.Unknown);
                LiftPower power = raw;
                if (Known)
                {
                    power = LiftServedRule.GuardPower(raw, Last, ref BackSince, frame, out bool recut);
                    if (recut)
                        Recuts++;
                }
                Known = true;
                Last = power;
                return power;
            }
        }

        private static void Pg0Constants()
        {
            const string W = "power-gate/PG-0";
            Check(LiftServedRule.PowerGatesService, W, "PowerGatesService is true (the oracles below assume power gates service)");
            Eq(512u, LiftServedRule.PowerBackHoldFrames, W, "PowerBackHoldFrames (DECISIONS 171 default)");
            Eq(uint.MaxValue, LiftServedRule.NoBackSince, W, "NoBackSince");
            Eq(1024u, LiftServedRule.PowerStreakStaleFrames, W, "PowerStreakStaleFrames = 2 × PowerBackHoldFrames");
        }

        private static readonly LiftPower[] s_Powers = { LiftPower.Unknown, LiftPower.Powered, LiftPower.NoPower };

        private static void Pg1Carries()
        {
            const string W = "power-gate/PG-1";
            var chords = new List<LiftServedRule.Chord> { new LiftServedRule.Chord { A = new float2(0f, 0f), B = new float2(0f, 1000f) } };
            int rows = 0;
            for (int c = 0; c < 2; c++)
            {
                bool complete = c == 1;
                foreach (LiftPower p in s_Powers)
                {
                    for (int k = 0; k < 2; k++)
                    {
                        bool closed = k == 1;
                        rows++;
                        bool want = complete && p != LiftPower.NoPower && !closed;
                        string tag = "complete " + complete + ", " + p + ", closed " + closed;
                        Eq(want, LiftServedRule.Carries(complete, p, closed), W, "Carries(" + tag + ")");
                        Eq(want, LiftServedRule.Carries(complete, p != LiftPower.NoPower, closed), W, "Carries(bool) (" + tag + ")");
                    }
                    var lift = new LiftServedRule.Lift
                    {
                        Id = new EntityRef(7, 1), Complete = complete, Power = p, TopY = 100f, FirstChord = 0, ChordCount = chords.Count,
                    };
                    Eq(LiftServedRule.Carries(lift.Complete, lift.Power, false), lift.Serves, W,
                       "Lift.Serves == Carries(Complete, Power, false) (complete " + complete + ", " + p + ")");
                }
            }
            Eq(12, rows, W, "row count");
        }

        private static List<KeyValuePair<uint, LiftPower>> Sweep(Reader r, Func<uint, Consumer> consumer, uint from, uint to, uint step)
        {
            var got = new List<KeyValuePair<uint, LiftPower>>();
            for (uint f = from; f < to; f += step)
                got.Add(new KeyValuePair<uint, LiftPower>(f, r.Read(consumer(f), f)));
            return got;
        }

        private static void Pg2aSteady()
        {
            const string W = "power-gate/PG-2a";
            var r = new Reader();
            int bad = 0, streakSet = 0;
            for (uint f = 0; f < 10000; f += 64)
            {
                if (r.Read(Consumer.Fed, f) != LiftPower.Powered)
                    bad++;
                if (r.BackSince != LiftServedRule.NoBackSince)
                    streakSet++;
            }
            Eq(0, bad, W, "reads that were not Powered over 10,000 powered frames");
            Eq(0, streakSet, W, "reads that left a streak running");
            Eq(0, r.Recuts, W, "recuts");
            uint bs = LiftServedRule.NoBackSince;
            Eq(LiftPower.Powered, LiftServedRule.GuardPower(LiftPower.Powered, LiftPower.Powered, ref bs, 12345u, out bool rc), W, "direct Powered/Powered");
            Check(!rc && bs == LiftServedRule.NoBackSince, W, "direct Powered/Powered leaves no streak and no recut");
        }

        private static void Pg2bCutAndBack()
        {
            const string W = "power-gate/PG-2b";
            const uint f0 = 6400, f1 = 12800;
            var r = new Reader();
            Func<uint, Consumer> consumer = f => f >= f0 && f < f1 ? Consumer.Cut : Consumer.Fed;
            List<KeyValuePair<uint, LiftPower>> got = Sweep(r, consumer, 0, 20000, 64);
            int bad = 0;
            uint firstBack = 0;
            foreach (KeyValuePair<uint, LiftPower> kv in got)
            {
                LiftPower want = kv.Key < f0 ? LiftPower.Powered : kv.Key < f1 + LiftServedRule.PowerBackHoldFrames ? LiftPower.NoPower : LiftPower.Powered;
                if (kv.Value != want)
                {
                    if (bad == 0)
                        Check(false, W, "first mismatch at frame " + kv.Key + ": " + kv.Value + ", want " + want);
                    bad++;
                }
                if (firstBack == 0 && kv.Key >= f1 && kv.Value == LiftPower.Powered)
                    firstBack = kv.Key;
            }
            Eq(0, bad, W, "mismatching reads (cut at 6,400, power back at 12,800)");
            Eq(f0, got.Find(kv => kv.Key == f0).Key, W, "a read lands on the cut frame");
            Eq(LiftPower.NoPower, got.Find(kv => kv.Key == f0).Value, W, "NoPower at the first read after the cut");
            Eq(f1 + 512u, firstBack, W, "first Powered read after the power came back");
            Eq(0, r.Recuts, W, "recuts (one clean streak)");

            // the inclusive boundary, direct
            uint bs = LiftServedRule.NoBackSince;
            Eq(LiftPower.NoPower, LiftServedRule.GuardPower(LiftPower.Powered, LiftPower.NoPower, ref bs, 1000u, out bool rc0), W, "streak start");
            Check(bs == 1000u && !rc0, W, "streak starts at the first Powered read (backSince " + bs + ")");
            Eq(LiftPower.NoPower, LiftServedRule.GuardPower(LiftPower.Powered, LiftPower.NoPower, ref bs, 1511u, out bool _), W, "511 frames in");
            Eq(1000u, bs, W, "streak kept at 511");
            Eq(LiftPower.Powered, LiftServedRule.GuardPower(LiftPower.Powered, LiftPower.NoPower, ref bs, 1512u, out bool rc2), W, "512 frames in (inclusive)");
            Check(bs == LiftServedRule.NoBackSince && !rc2, W, "streak closed at 512");
        }

        /// <summary>The warning on for 128 of every 384 frames (DispatchElectricitySystem.cs:126-142 at its fastest: short,
        /// short + warning, full).</summary>
        private static Consumer Brownout(uint f, uint start)
        {
            if (f < start)
                return Consumer.Cut;
            uint ph = (f - start) % 384u;
            return ph < 128u ? Consumer.Cut : Consumer.Fed;
        }

        private static void Pg2cBrownout()
        {
            const string W = "power-gate/PG-2c";
            const uint start = 1024, cycles = 20;
            var r = new Reader();
            r.Read(Consumer.Fed, 0);
            r.Read(Consumer.Cut, 512);
            int notNoPower = 0;
            for (uint f = start; f < start + cycles * 384u; f += 64)
                if (r.Read(Brownout(f, start), f) != LiftPower.NoPower)
                    notNoPower++;
            Eq(0, notNoPower, W, "reads that were not NoPower through 20 brownout cycles (64-frame reader)");
            Eq((int)cycles - 1, r.Recuts, W, "recuts (one per cycle after the first)");

            // a 256-frame reader (the attraction / charge polls) at every 64-frame phase of the 384-frame cycle
            for (uint phase = 0; phase < 384; phase += 64)
            {
                var slow = new Reader();
                slow.Read(Consumer.Fed, 0);
                slow.Read(Consumer.Cut, 512);
                int bad = 0;
                for (uint f = start + phase; f < start + cycles * 384u; f += 256)
                    if (slow.Read(Brownout(f, start), f) != LiftPower.NoPower)
                        bad++;
                Eq(0, bad, W, "256-frame reader at phase " + phase + ": reads that were not NoPower");
            }
        }

        private static void Pg2dSlowReader()
        {
            const string W = "power-gate/PG-2d";
            var r = new Reader();
            r.Read(Consumer.Fed, 0);
            Func<uint, Consumer> consumer = f => f < 1000 ? Consumer.Cut : Consumer.Fed;
            Eq(LiftPower.NoPower, r.Read(consumer(256), 256), W, "256: cut");
            Eq(LiftPower.NoPower, r.Read(consumer(512), 512), W, "512: cut");
            Eq(LiftPower.NoPower, r.Read(consumer(768), 768), W, "768: cut");
            Eq(LiftPower.NoPower, r.Read(consumer(1024), 1024), W, "1,024: first read seeing power (streak starts)");
            Eq(1024u, r.BackSince, W, "streak start = the first read that saw power, not the frame power came back");
            Eq(LiftPower.NoPower, r.Read(consumer(1280), 1280), W, "1,280: 256 frames in");
            Eq(LiftPower.Powered, r.Read(consumer(1536), 1536), W, "1,536: 512 frames in");
        }

        private static void Pg2eWrap()
        {
            const string W = "power-gate/PG-2e";
            uint bs = 0xFFFFFF00u;
            Eq(LiftPower.Powered, LiftServedRule.GuardPower(LiftPower.Powered, LiftPower.NoPower, ref bs, 0x00000100u, out bool _), W,
               "backSince 0xFFFFFF00, now 0x100 (512 across the wrap)");
            bs = 0xFFFFFF00u;
            Eq(LiftPower.NoPower, LiftServedRule.GuardPower(LiftPower.Powered, LiftPower.NoPower, ref bs, 0x000000FFu, out bool _), W,
               "backSince 0xFFFFFF00, now 0xFF (511 across the wrap)");
            Eq(0xFFFFFF00u, bs, W, "streak kept across the wrap");
        }

        private static void Pg2fUnknown()
        {
            const string W = "power-gate/PG-2f";
            foreach (LiftPower last in s_Powers)
            {
                uint bs = 777u;
                Eq(LiftPower.Unknown, LiftServedRule.GuardPower(LiftPower.Unknown, last, ref bs, 2000u, out bool rc), W, "raw Unknown, last " + last);
                Check(bs == LiftServedRule.NoBackSince && !rc, W, "raw Unknown clears the streak without a recut (last " + last + ")");
            }
        }

        private static void Pg2gNotFromNoPower()
        {
            const string W = "power-gate/PG-2g";
            foreach (LiftPower last in new[] { LiftPower.Powered, LiftPower.Unknown })
                foreach (LiftPower raw in s_Powers)
                {
                    uint bs = LiftServedRule.NoBackSince;
                    Eq(raw, LiftServedRule.GuardPower(raw, last, ref bs, 4242u, out bool rc), W, "raw " + raw + ", last " + last);
                    Check(bs == LiftServedRule.NoBackSince && !rc, W, "no streak, no recut (raw " + raw + ", last " + last + ")");
                }
            // a running streak is recut by a raw NoPower
            uint s = 100u;
            Eq(LiftPower.NoPower, LiftServedRule.GuardPower(LiftPower.NoPower, LiftPower.NoPower, ref s, 300u, out bool recut), W, "raw NoPower mid-streak");
            Check(recut && s == LiftServedRule.NoBackSince, W, "raw NoPower mid-streak recuts and clears");
        }

        private static void Pg2hOneDispatchWindow()
        {
            const string W = "power-gate/PG-2h";
            const uint win = 2048 + 64; // the window starts on a 64-frame read, off the 256 grid
            Func<uint, Consumer> consumer = f => f >= win && f < win + 128 ? Consumer.Cut : Consumer.Fed;
            var fast = new Reader();
            uint firstNo = 0, lastNo = 0;
            foreach (KeyValuePair<uint, LiftPower> kv in Sweep(fast, consumer, 0, 6000, 64))
            {
                if (kv.Value != LiftPower.NoPower)
                    continue;
                if (firstNo == 0)
                    firstNo = kv.Key;
                lastNo = kv.Key;
            }
            Eq(win, firstNo, W, "64-frame reader: NoPower from the window's first read");
            Eq(win + 128 + 512 - 64, lastNo, W, "64-frame reader: last NoPower read (the guard runs from the first powered read)");
            Check(lastNo + 64 - firstNo >= 512, W, "64-frame reader: NoPower for at least 512 frames (" + (lastNo + 64 - firstNo) + ")");
            var slow = new Reader();
            int slowNo = 0;
            foreach (KeyValuePair<uint, LiftPower> kv in Sweep(slow, consumer, 0, 6000, 256))
                if (kv.Value != LiftPower.Powered)
                    slowNo++;
            Eq(0, slowNo, W, "256-frame reader whose reads miss the window: never NoPower (identity per path, MUST-4)");
        }

        private static void Pg2iFirstRead()
        {
            const string W = "power-gate/PG-2i";
            var a = new Reader();
            Eq(LiftPower.NoPower, a.Read(Consumer.Cut, 100), W, "first read of a load, cut → NoPower at once");
            var b = new Reader();
            Eq(LiftPower.Powered, b.Read(Consumer.Fed, 100), W, "first read of a load, fed → Powered at once (R-6, fail open)");
            Check(b.BackSince == LiftServedRule.NoBackSince, W, "first read starts no streak");
        }

        private static void Pg3F2Composition()
        {
            const string W = "power-gate/PG-3";
            // 60 % with the warning: the way back needs 3/4 (4 x 900 = 3,600 < 4,500), so raw NoPower whatever the time.
            var r60 = new Reader();
            r60.Read(Consumer.Cut, 0);
            int not60 = 0;
            for (uint f = 64; f < 5064; f += 64)
                if (r60.Read(Consumer.At(Warn, 900), f) != LiftPower.NoPower)
                    not60++;
            Eq(0, not60, W, "60 % with the warning, last NoPower: reads that were not NoPower over 5,000 frames");
            Eq(0, r60.Recuts, W, "60 %: no streak ever starts, so no recut");

            // 80 % with the warning: raw Powered (4 x 1,200 = 4,800 >= 4,500), so NoPower for 512 frames, then Powered; the half rule
            // (2 x 1,200 >= 1,500) keeps it Powered.
            var r80 = new Reader();
            r80.Read(Consumer.Cut, 0);
            Eq(LiftPower.NoPower, r80.Read(Consumer.At(Warn, 1200), 64), W, "80 %: streak start");
            Eq(LiftPower.NoPower, r80.Read(Consumer.At(Warn, 1200), 64 + 511), W, "80 %: 511 frames in");
            Eq(LiftPower.Powered, r80.Read(Consumer.At(Warn, 1200), 64 + 512), W, "80 %: 512 frames in");
            Eq(LiftPower.Powered, r80.Read(Consumer.At(Warn, 900), 64 + 576), W, "back, then 60 % with the warning: F2's half rule keeps Powered");
            Eq(LiftPower.NoPower, r80.Read(Consumer.At(Warn, 700), 64 + 640), W, "under half with the warning: NoPower at once");

            // the warning cleared (fulfilled 0 even: before electricity unlocks, DispatchElectricitySystem.cs:61, :106-109)
            var rw = new Reader();
            rw.Read(Consumer.Cut, 0);
            Eq(LiftPower.NoPower, rw.Read(Consumer.At(0, 0), 128), W, "warning cleared: streak start");
            Eq(LiftPower.NoPower, rw.Read(Consumer.At(0, 0), 128 + 448), W, "warning cleared: 448 frames in");
            Eq(LiftPower.Powered, rw.Read(Consumer.At(0, 0), 128 + 512), W, "warning cleared: 512 frames in");
        }

        private static void Pg4ExpireStreak()
        {
            const string W = "power-gate/PG-4";
            uint none = LiftServedRule.NoBackSince;
            Eq(none, LiftServedRule.ExpireStreak(none, 0u, 1000000u), W, "no streak stays none");
            Eq(500u, LiftServedRule.ExpireStreak(500u, 600u, 600u + 1024u), W, "gap 1,024 keeps the streak");
            Eq(none, LiftServedRule.ExpireStreak(500u, 600u, 600u + 1025u), W, "gap 1,025 drops the streak");
            Eq(500u, LiftServedRule.ExpireStreak(500u, 600u, 664u), W, "gap 64 keeps the streak");
            Eq(0xFFFFFF80u, LiftServedRule.ExpireStreak(0xFFFFFF80u, 0xFFFFFFC0u, 0x00000100u), W, "gap 320 across the wrap keeps");
            Eq(none, LiftServedRule.ExpireStreak(0xFFFFFF80u, 0xFFFFFFC0u, 0x00000500u), W, "gap 1,344 across the wrap drops");
        }
    }
}
