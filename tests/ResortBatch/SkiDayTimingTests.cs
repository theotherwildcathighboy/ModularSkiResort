// SkiDayTimingTests — Round S1 test file for tests/ResortBatch (ski-days spec ch2-system.md §14, ST1-ST9: the pure
// SkiDayTiming). Owner: stream B (docs/research/ski-days/s1-spec.md §4.3). Created empty by stage 0 (2026-10-05,
// s1-spec §4.2 step 9): the e1-spec R9 convention — `public static class SkiDayTimingTests` with
// `public static void Run(Func<bool, string, string, bool> check)` and its own Near; Program.Main calls
// SkiDayTimingTests.Run(Check). Oracles are literal hand values (ch2 §14). Stream B wrote the rows (2026-10-05).
//
// Covers: the constants and enum literals (ST0); RecentBehaviorSlice (ST1, ST1b: CitizenBehaviorSystem at
// interval 16, offset 11); SkipChunk (ST2: LeisureSystem 64 + the recent CBS slice); IsLastPass (ST3); FramesFor /
// HoursFor (ST4, ST4b: the uint.MaxValue clamp); Resolve (ST5: every on-the-way and on-site row); HourStep (ST6);
// NextLeisureVisit (ST7: the resident roll key, s1-spec N2); TrackIndex (ST8: the job's index guard, ch2-RF2-1);
// SameBits (ST9). The rows are followed by property sweeps (ST10-ST13) that tie the helpers to each other and to
// SkiDayRules.SliceOf / SkipSlice (one decision = one NextLeisureVisit; IsLastPass ⇔ next visit is one interval
// away; the recent-CBS window is exactly the 8 frames from a run; SkipChunk ⊇ SkipSlice). Fixer pass (review
// SIM-1): IsLastSighting rows (ST14) and the sweep over all 64 LeisureSystem offsets (ST15). Not testable headless
// (ECS): the jobs, the claim itself, the car-reserve handshake — covered by the logs, the DUMP and field test 9.
// Round M1 "measure first" (docs/research/ski-days/measure/m1-spec.md §4; builder A, 2026-10-05): ST16 PlaceOf,
// ST17 EndCauseOf, ST18 ExitOfPurpose / TryExit and the two watch constants, ST19 MinuteOfDay / ClockMinuteAfter,
// ST20 GuestScanSaw, ST21 CarReserveOf, ST22 ClockBin / StayBin / ClosestBand, ST23 MarkHoursPerMonth, ST24 WhyOf,
// ST25 TryLate and HasMarker. Not testable headless: the ResolveJob sampling, the ring, the measure rows, the ledger
// check — covered by the logs and the DUMP in field test 10. Fixer pass: ST26 DepartedNow (review M1-ECS-2: a guest
// on the road with GoingHome has departed).

using System;
using SkiResortMod.Systems;
using Outcome = SkiResortMod.Systems.SkiDayTiming.ClaimOutcome;
using Place = SkiResortMod.Systems.SkiDayTiming.Place;
using EndCause = SkiResortMod.Systems.SkiDayTiming.EndCause;
using ExitWay = SkiResortMod.Systems.SkiDayTiming.ExitWay;
using GoneWhy = SkiResortMod.Systems.SkiDayTiming.GoneWhy;
using LateResult = SkiResortMod.Systems.SkiDayTiming.LateResult;
using CarReserve = SkiResortMod.Systems.SkiDayTiming.CarReserve;
using ScanVerdict = SkiResortMod.Systems.SkiDayTiming.ScanVerdict;

namespace ResortBatch
{
    public static class SkiDayTimingTests
    {
        public static void Run(Func<bool, string, string, bool> check)
        {
            Constants(check);
            RecentBehavior(check);
            SkipAndLast(check);
            FramesAndHours(check);
            Resolve(check);
            HourStep(check);
            NextVisit(check);
            TrackIndex(check);
            SameBits(check);
            Properties(check);
            LastSighting(check);   // fixer pass (review SIM-1)
            // Round M1 (m1-spec §4)
            PlaceRows(check);      // ST16
            EndCauseRows(check);   // ST17
            ExitRows(check);       // ST18
            ClockRows(check);      // ST19
            ScanRows(check);       // ST20
            ReserveRows(check);    // ST21
            BinRows(check);        // ST22
            MarkHourRows(check);   // ST23
            WhyRows(check);        // ST24
            LateRows(check);       // ST25
            DepartRows(check);     // ST26 (fixer M1-ECS-2)
        }

        private static bool Near(double a, double b, double eps)
        {
            return Math.Abs(a - b) <= eps;
        }

        // ── ST0 constants ───────────────────────────────────────────────────────────────────────
        private static void Constants(Func<bool, string, string, bool> check)
        {
            const string w = "skiday-timing ST0";
            check(SkiDayTiming.MaxFramesPerRenderFrame == 8, w, "MaxFramesPerRenderFrame " + SkiDayTiming.MaxFramesPerRenderFrame);
            check(SkiDayTiming.OnSiteGraceFrames == 21845u, w, "OnSiteGraceFrames " + SkiDayTiming.OnSiteGraceFrames);
            check(SkiDayTiming.OnSiteGraceFrames == SkiDayTiming.FramesFor(2.0), w, "OnSiteGraceFrames = FramesFor(2)");
            check((byte)Outcome.OnTheWay == 0 && (byte)Outcome.Arrived == 1 && (byte)Outcome.OnSite == 2 && (byte)Outcome.Left == 3 &&
                  (byte)Outcome.Gone == 4 && (byte)Outcome.Retargeted == 5 && (byte)Outcome.Expired == 6, w, "ClaimOutcome literals 0..6");
        }

        // ── ST1, ST1b RecentBehaviorSlice(F, 16, 11) ────────────────────────────────────────────
        private static void RecentBehavior(Func<bool, string, string, bool> check)
        {
            const string w = "skiday-timing ST1";
            Recent(check, w, 1000000u, 16, 11, true, 3u);    // (1,000,000 − 11) mod 16 = 5 → last 999,995; 62,499 & 15 = 3
            Recent(check, w, 1000011u, 16, 11, true, 4u);    // a run at F: diff 0; 62,500 & 15 = 4
            Recent(check, w, 1000018u, 16, 11, true, 4u);    // last 1,000,011, diff 7
            Recent(check, w, 1000019u, 16, 11, false, 0u);   // diff 8 is not < 8
            Recent(check, w, 1000064u, 16, 11, true, 7u);    // last 1,000,059; 62,503 & 15 = 7
            Recent(check, w, 5u, 16, 11, false, 0u);         // 5 < offset 11: no run yet
            Recent(check, "skiday-timing ST1b", 1000u, 0, 11, false, 0u);    // interval guard (s1-spec M-3)
            Recent(check, "skiday-timing ST1b", 1000u, -16, 11, false, 0u);  // negative interval
            Recent(check, "skiday-timing ST1b", 1000u, 16, -1, false, 0u);   // negative offset
        }

        private static void Recent(Func<bool, string, string, bool> check, string w, uint frame, int interval, int offset,
                                   bool expected, uint expectedSlice)
        {
            bool got = SkiDayTiming.RecentBehaviorSlice(frame, interval, offset, out uint slice);
            check(got == expected && (!expected || slice == expectedSlice), w,
                  $"RecentBehaviorSlice({frame}, {interval}, {offset}) = ({got}, {slice}), expected ({expected}, {expectedSlice})");
        }

        // ── ST2 SkipChunk, ST3 IsLastPass ───────────────────────────────────────────────────────
        private static void SkipAndLast(Func<bool, string, string, bool> check)
        {
            const string w2 = "skiday-timing ST2";
            // LS slice at 1,000,000: 15,625 & 15 = 9; CBS does not run at F (F mod 16 = 0), its recent slice is 3.
            check(SkiDayTiming.SkipChunk(9u, 1000000u, 64, 16, 11), w2, "SkipChunk(9) (LeisureSystem's slice)");
            check(SkiDayTiming.SkipChunk(3u, 1000000u, 64, 16, 11), w2, "SkipChunk(3) (recent CBS slice)");
            check(!SkiDayTiming.SkipChunk(10u, 1000000u, 64, 16, 11), w2, "!SkipChunk(10)");
            check(!SkiDayTiming.SkipChunk(4u, 1000000u, 64, 16, 11), w2, "!SkipChunk(4)");
            check(!SkiDayRules.SkipSlice(3u, 1000000u, 64, 16, 11), w2, "SkipSlice(3) is false: only SkipChunk adds the recent slice (V2)");

            const string w3 = "skiday-timing ST3";
            check(SkiDayTiming.IsLastPass(10u, 1000000u, 64), w3, "IsLastPass(10, 1,000,000, 64): (9 + 1) & 15 = 10");
            check(!SkiDayTiming.IsLastPass(9u, 1000000u, 64), w3, "!IsLastPass(9, 1,000,000, 64)");
            check(SkiDayTiming.IsLastPass(0u, 960u, 64), w3, "IsLastPass(0, 960, 64): (15 + 1) & 15 = 0");
        }

        // ── ST4, ST4b FramesFor / HoursFor ──────────────────────────────────────────────────────
        private static void FramesAndHours(Func<bool, string, string, bool> check)
        {
            const string w = "skiday-timing ST4";
            Frames(check, w, 2.0, 21845u);              // 2 × 10,922.667 = 21,845.33
            Frames(check, w, 3.9, 42598u);              // 3.9 × 10,922.667 = 42,598.4
            Frames(check, w, 24.0, 262144u);            // one game day
            Frames(check, w, -1.0, 0u);
            Frames(check, w, double.NaN, 0u);
            Frames(check, w, 0.0, 0u);
            double h = SkiDayTiming.HoursFor(30000u);   // 30,000 × 24 / 262,144 = 2.74658203125
            check(Near(h, 2.74658203125, 1e-12), w, "HoursFor(30,000) " + h.ToString("R"));
            check(SkiDayTiming.HoursFor(262144u) == 24.0, w, "HoursFor(262,144) = 24");

            const string wb = "skiday-timing ST4b";
            Frames(check, wb, double.PositiveInfinity, 4294967295u);
            Frames(check, wb, 1e9, 4294967295u);
            Frames(check, wb, 393215.0, 4294956373u);   // 393,215 × 262,144 / 24 = 4,294,956,373.3
            Frames(check, wb, 393216.0, 4294967295u);   // above uint.MaxValue / FramesPerHour = 393,215.9999
        }

        private static void Frames(Func<bool, string, string, bool> check, string w, double hours, uint expected)
        {
            uint got = SkiDayTiming.FramesFor(hours);
            check(got == expected, w, $"FramesFor({hours:R}) = {got}, expected {expected}");
        }

        // ── ST5 Resolve, deadline 1,075,366 ─────────────────────────────────────────────────────
        private static void Resolve(Func<bool, string, string, bool> check)
        {
            const string w = "skiday-timing ST5";
            const uint dl = 1075366u;
            // (onSite, hasLeisure, Leisure = lodge, at the lodge, now) → outcome
            Res(check, w, false, true, true, true, 1050000u, dl, Outcome.Arrived);
            Res(check, w, false, true, true, true, 1080000u, dl, Outcome.Arrived);      // arrival wins over the deadline
            Res(check, w, false, true, true, false, 1050000u, dl, Outcome.OnTheWay);
            Res(check, w, false, true, true, false, 1075366u, dl, Outcome.Expired);     // vanilla's >=
            Res(check, w, false, false, false, false, 1050000u, dl, Outcome.Gone);
            Res(check, w, false, false, false, true, 1050000u, dl, Outcome.Gone);       // at the lodge without Leisure
            Res(check, w, false, true, false, false, 1050000u, dl, Outcome.Retargeted);
            Res(check, w, false, false, false, false, 1080000u, dl, Outcome.Expired);   // expired before gone
            Res(check, w, false, true, false, false, 1080000u, dl, Outcome.Expired);
            Res(check, w, true, true, true, true, 1080000u, dl, Outcome.OnSite);        // < 1,075,366 + 21,845 = 1,097,211
            Res(check, w, true, true, true, true, 1097211u, dl, Outcome.Left);
            Res(check, w, true, false, false, true, 1050000u, dl, Outcome.Left);
            Res(check, w, true, true, false, true, 1050000u, dl, Outcome.Left);
            Res(check, w, true, true, true, false, 1050000u, dl, Outcome.Left);
            // the grace sum does not wrap at the top of the frame range (64-bit compare)
            Res(check, w, true, true, true, true, 4294967290u, 4294967000u, Outcome.OnSite);
        }

        private static void Res(Func<bool, string, string, bool> check, string w, bool onSite, bool hasL, bool atL, bool at,
                                uint now, uint deadline, Outcome expected)
        {
            Outcome got = SkiDayTiming.Resolve(onSite, hasL, atL, at, now, deadline);
            check(got == expected, w, $"Resolve({onSite}, {hasL}, {atL}, {at}, {now}, {deadline}) = {got}, expected {expected}");
        }

        // ── ST6 HourStep ────────────────────────────────────────────────────────────────────────
        private static void HourStep(Func<bool, string, string, bool> check)
        {
            const string w = "skiday-timing ST6";
            Step(check, w, -1, 5, 12, 24);
            Step(check, w, 5, 5, 12, 0);
            Step(check, w, 5, 6, 12, 1);
            Step(check, w, 287, 0, 12, 1);     // 12 days = 288 hours: 287 + 1 wraps to 0
            Step(check, w, 5, 7, 12, 24);
            Step(check, w, 6, 5, 12, 24);
            Step(check, w, 23, 0, 0, 1);       // days 0 → 1 → 24 hours
            Step(check, w, int.MaxValue, 0, 12, 24);   // no overflow on prev + 1
        }

        private static void Step(Func<bool, string, string, bool> check, string w, int prev, int key, int days, int expected)
        {
            int got = SkiDayTiming.HourStep(prev, key, days);
            check(got == expected, w, $"HourStep({prev}, {key}, {days}) = {got}, expected {expected}");
        }

        // ── ST7 NextLeisureVisit (LS 64, slice 12) ──────────────────────────────────────────────
        private static void NextVisit(Func<bool, string, string, bool> check)
        {
            const string w = "skiday-timing ST7";
            Visit(check, w, 12u, 1000000u, 64, 1000192u);    // slice 9, d 3 → +192
            Visit(check, w, 12u, 1000064u, 64, 1000192u);    // slice 10, d 2 → +128
            Visit(check, w, 12u, 1000128u, 64, 1000192u);    // slice 11, d 1 → +64
            Visit(check, w, 12u, 1000256u, 64, 1001216u);    // slice 13, d (12 − 13) & 15 = 15 → +960
            Visit(check, w, 12u, 1000192u, 64, 1000192u);    // slice 12, d 0 (LeisureSystem takes it this frame)
            Visit(check, w, 12u, 1000000u, 0, 1000000u);     // interval guard
        }

        private static void Visit(Func<bool, string, string, bool> check, string w, uint slice, uint frame, int li, uint expected)
        {
            uint got = SkiDayTiming.NextLeisureVisit(slice, frame, li);
            check(got == expected, w, $"NextLeisureVisit({slice}, {frame}, {li}) = {got}, expected {expected}");
        }

        // ── ST8 TrackIndex ──────────────────────────────────────────────────────────────────────
        private static void TrackIndex(Func<bool, string, string, bool> check)
        {
            const string w = "skiday-timing ST8";
            check(SkiDayTiming.TrackIndex(0, 0) == 0, w, "TrackIndex(0, 0) " + SkiDayTiming.TrackIndex(0, 0));
            check(SkiDayTiming.TrackIndex(2, 1) == 7, w, "TrackIndex(2, 1) " + SkiDayTiming.TrackIndex(2, 1));
            check(SkiDayTiming.TrackIndex(1, 2) == 5, w, "TrackIndex(1, 2) " + SkiDayTiming.TrackIndex(1, 2));
            check(SkiDayTiming.TrackIndex(-1, 0) == -1, w, "TrackIndex(-1, 0) " + SkiDayTiming.TrackIndex(-1, 0));
            check(SkiDayTiming.TrackIndex(0, 3) == -1, w, "TrackIndex(0, 3) " + SkiDayTiming.TrackIndex(0, 3));
            check(SkiDayTiming.TrackIndex(0, (byte)SkiDayRules.TravelClass.None) == -1, w, "TrackIndex(0, TravelClass.None) is -1");
        }

        // ── ST9 SameBits ────────────────────────────────────────────────────────────────────────
        private static void SameBits(Func<bool, string, string, bool> check)
        {
            const string w = "skiday-timing ST9";
            check(SkiDayTiming.SameBits(float.NaN, float.NaN), w, "SameBits(NaN f, NaN f)");
            check(!SkiDayTiming.SameBits(0f, -0f), w, "!SameBits(0f, -0f)");
            check(SkiDayTiming.SameBits(1f, 1f), w, "SameBits(1f, 1f)");
            check(!SkiDayTiming.SameBits(1.0, 1.0000000000000002), w, "!SameBits(1.0, 1.0000000000000002)");
            check(SkiDayTiming.SameBits(double.NaN, double.NaN), w, "SameBits(NaN, NaN)");
            check(!SkiDayTiming.SameBits(0.0, -0.0), w, "!SameBits(0.0, -0.0)");
        }

        // ── ST10-ST13 property sweeps (tie the helpers to each other and to SkiDayRules) ─────────
        private static void Properties(Func<bool, string, string, bool> check)
        {
            // ST10: for every slice and pass frame, NextLeisureVisit lands on that slice, on LeisureSystem's frames,
            // within 15 intervals, and is the same at every earlier pass of the same decision (one roll per
            // decision, V7); IsLastPass ⇔ the visit is exactly one interval away.
            const int li = 64;
            int bad10 = 0, bad11 = 0;
            string first10 = null, first11 = null;
            for (uint f = 999424u; f < 999424u + 64u * 40u; f += 64u)
            {
                for (uint s = 0; s < 16u; s++)
                {
                    uint v = SkiDayTiming.NextLeisureVisit(s, f, li);
                    bool ok = SkiDayRules.SliceOf(v, li) == s && v >= f && v - f <= 15u * li && (v - f) % li == 0;
                    for (uint back = 64u; ok && back <= v - f; back += 64u)
                        ok = SkiDayTiming.NextLeisureVisit(s, v - back, li) == v;
                    if (!ok)
                    {
                        bad10++;
                        first10 = first10 ?? $"slice {s} frame {f} → {v}";
                    }
                    if (SkiDayTiming.IsLastPass(s, f, li) != (v - f == (uint)li))
                    {
                        bad11++;
                        first11 = first11 ?? $"slice {s} frame {f}";
                    }
                }
            }
            check(bad10 == 0, "skiday-timing ST10", $"NextLeisureVisit sweep: {bad10} bad (first: {first10})");
            check(bad11 == 0, "skiday-timing ST11", $"IsLastPass ⇔ next visit one interval away: {bad11} bad (first: {first11})");

            // ST12: CitizenBehaviorSystem 16 / 11 — the recent window is true exactly for (f − 11) mod 16 < 8, and its
            // slice is SliceOf(the run frame, 16).
            int bad12 = 0;
            string first12 = null;
            for (uint f = 11u; f < 11u + 16u * 20u; f++)
            {
                bool got = SkiDayTiming.RecentBehaviorSlice(f, 16, 11, out uint slice);
                uint diff = (f - 11u) % 16u;
                bool expect = diff < 8u;
                if (got != expect || (expect && slice != SkiDayRules.SliceOf(f - diff, 16)))
                {
                    bad12++;
                    first12 = first12 ?? $"frame {f}";
                }
            }
            check(bad12 == 0, "skiday-timing ST12", $"recent CBS window sweep: {bad12} bad (first: {first12})");

            // ST13: SkipChunk ⊇ SkipSlice (it only adds the recent slice) for every slice over 64 consecutive frames.
            int bad13 = 0;
            for (uint f = 1000000u; f < 1000064u; f++)
            {
                for (uint s = 0; s < 16u; s++)
                {
                    if (SkiDayRules.SkipSlice(s, f, 64, 16, 11) && !SkiDayTiming.SkipChunk(s, f, 64, 16, 11))
                        bad13++;
                }
            }
            check(bad13 == 0, "skiday-timing ST13", $"SkipChunk covers SkipSlice: {bad13} bad");
        }

        // ── ST14, ST15 IsLastSighting (fixer pass, review SIM-1; CBS 16 / 11, LeisureSystem 64) ───────
        private static void LastSighting(Func<bool, string, string, bool> check)
        {
            const string w = "skiday-timing ST14";
            // LeisureSystem offset 0: slice 7 is LeisureSystem's at 448; the pass at 384 skips it (CBS ran at 379 on
            // slice (379 / 16) & 15 = 7, 5 frames back), so the pass at 320 is its last sighting.
            Sight(check, w, 7u, 320u, 64, true);
            Sight(check, w, 7u, 256u, 64, false);       // the pass at 320 still sees it (its recent CBS slice is 3)
            Sight(check, w, 7u, 384u, 64, true);        // = IsLastPass (that pass skips the chunk, row below)
            Sight(check, w, 10u, 1000000u, 64, true);   // = IsLastPass(10, 1,000,000, 64) (ST3)
            Sight(check, w, 11u, 1000000u, 64, false);  // d 2: the pass at 1,000,064 sees slice 11 (recent CBS slice 7)
            Sight(check, w, 9u, 1000000u, 64, false);   // LeisureSystem's own slice this frame (d 0)
            Sight(check, w, 7u, 320u, 0, false);        // interval guard
            check(SkiDayTiming.SkipChunk(7u, 384u, 64, 16, 11), w, "SkipChunk(7, 384, 64, 16, 11): the recent CBS slice is 7");

            // ST15: at every LeisureSystem offset q (0..63), for 48 visits each (three rounds of the 16 slices), exactly
            // one of the 15 passes before the visit sees the slice unskipped AND calls it the last sighting (a decision
            // enters a pool once); the last sighting is not the last pass at 32 offsets — the reviewer's probe P1 and
            // the checker's recomputation (q 0-2, 11-18, 27-34, 43-50, 59-63).
            int bad15 = 0, differ = 0;
            string first15 = null;
            for (uint q = 0u; q < 64u; q++)
            {
                bool offsetDiffers = false;
                for (uint j = 16u; j < 64u; j++)
                {
                    uint v = 1048576u + q + 64u * j;   // LeisureSystem's visit
                    uint s = SkiDayRules.SliceOf(v, 64);
                    int counted = 0;
                    for (uint k = 1u; k <= 15u; k++)
                    {
                        uint f = v - 64u * k;
                        if (SkiDayTiming.SkipChunk(s, f, 64, 16, 11) || !SkiDayTiming.IsLastSighting(s, f, 64, 16, 11))
                            continue;
                        counted++;
                        if (!SkiDayTiming.IsLastPass(s, f, 64))
                            offsetDiffers = true;
                    }
                    if (counted != 1)
                    {
                        bad15++;
                        first15 = first15 ?? $"offset {q}, visit {v}, slice {s}: {counted} counted";
                    }
                }
                if (offsetDiffers)
                    differ++;
            }
            check(bad15 == 0, "skiday-timing ST15", $"one counted sighting per visit: {bad15} bad (first: {first15})");
            check(differ == 32, "skiday-timing ST15", $"offsets where the last sighting is an earlier pass: {differ}, expected 32");
        }

        private static void Sight(Func<bool, string, string, bool> check, string w, uint slice, uint frame, int li, bool expected)
        {
            bool got = SkiDayTiming.IsLastSighting(slice, frame, li, 16, 11);
            check(got == expected, w, $"IsLastSighting({slice}, {frame}, {li}, 16, 11) = {got}, expected {expected}");
        }

        // ══ Round M1 "measure first" (m1-spec §4; literal oracles) ═══════════════════════════════════

        // ── ST16 PlaceOf(gone, inBuilding, atOrigin, atLodge, departed) ───────────────────────────
        private static void PlaceRows(Func<bool, string, string, bool> check)
        {
            const string w = "skiday-timing ST16";
            check((byte)Place.HomeNeverLeft == 0 && (byte)Place.HomeCameBack == 1 && (byte)Place.Road == 2 && (byte)Place.OtherBuilding == 3 &&
                  (byte)Place.Lodge == 4 && (byte)Place.Deleted == 5 && SkiDayTiming.PlaceCount == 6, w, "Place literals 0..5");
            Pl(check, w, true, false, false, false, false, Place.Deleted);
            Pl(check, w, false, false, false, false, false, Place.Road);
            Pl(check, w, false, false, false, false, true, Place.Road);
            Pl(check, w, false, true, false, true, false, Place.Lodge);
            Pl(check, w, false, true, false, true, true, Place.Lodge);
            Pl(check, w, false, true, true, false, false, Place.HomeNeverLeft);
            Pl(check, w, false, true, true, false, true, Place.HomeCameBack);
            Pl(check, w, false, true, false, false, false, Place.OtherBuilding);
            Pl(check, w, false, true, false, false, true, Place.OtherBuilding);
            // gone beats every other flag
            int bad = 0;
            for (int m = 0; m < 16; m++)
            {
                if (SkiDayTiming.PlaceOf(true, (m & 1) != 0, (m & 2) != 0, (m & 4) != 0, (m & 8) != 0) != Place.Deleted)
                    bad++;
            }
            check(bad == 0, w, $"gone → Deleted for all 16 other combinations: {bad} bad");
        }

        private static void Pl(Func<bool, string, string, bool> check, string w, bool gone, bool inB, bool atO, bool atL, bool dep, Place expected)
        {
            Place got = SkiDayTiming.PlaceOf(gone, inB, atO, atL, dep);
            check(got == expected, w, $"PlaceOf({gone}, {inB}, {atO}, {atL}, {dep}) = {got}, expected {expected}");
        }

        // ── ST17 EndCauseOf (now 1,000,000, deadline 1,000,100) ───────────────────────────────────
        private static void EndCauseRows(Func<bool, string, string, bool> check)
        {
            const string w = "skiday-timing ST17";
            check((byte)EndCause.DeadlineOwn == 0 && (byte)EndCause.DeadlineClosing == 1 && (byte)EndCause.NeedFilled == 2 &&
                  (byte)EndCause.NewOuting == 3 && (byte)EndCause.Removed == 4 && (byte)EndCause.WalkedOut == 5 &&
                  (byte)EndCause.PastGrace == 6 && (byte)EndCause.Deleted == 7 && SkiDayTiming.EndCauseCount == 8, w, "EndCause literals 0..7");
            check(SkiDayTiming.NeedFilledCounter == 250, w, "NeedFilledCounter 250");
            const uint now = 1000000u, dl = 1000100u;
            // (gone, hasLeisure, leisureAtLodge, atLodge, deadline, closing, counter)
            Ec(check, w, true, true, true, true, dl, true, 0, EndCause.Deleted);
            Ec(check, w, false, true, true, true, dl, true, 0, EndCause.PastGrace);
            Ec(check, w, false, true, true, true, 999999u, false, 255, EndCause.PastGrace);   // there wins over the deadline
            Ec(check, w, false, false, false, true, 999999u, true, 0, EndCause.DeadlineClosing);
            Ec(check, w, false, false, false, true, 999999u, false, 0, EndCause.DeadlineOwn);
            Ec(check, w, false, false, false, false, 1000000u, true, 0, EndCause.DeadlineClosing);   // now ≥ deadline (equal)
            Ec(check, w, false, true, false, true, dl, false, 0, EndCause.NewOuting);
            Ec(check, w, false, false, false, true, dl, false, 253, EndCause.NeedFilled);
            Ec(check, w, false, false, false, true, dl, false, 250, EndCause.NeedFilled);
            Ec(check, w, false, false, false, true, dl, false, 249, EndCause.Removed);
            Ec(check, w, false, true, true, false, dl, false, 0, EndCause.WalkedOut);
            Ec(check, w, false, false, false, true, 999999u, false, 253, EndCause.DeadlineOwn);   // the deadline beats NeedFilled
            check(SkiDayTiming.EndCauseOf(false, false, false, true, now, dl, false, 0) == EndCause.Removed, w, "no Leisure, counter 0 → Removed");
        }

        private static void Ec(Func<bool, string, string, bool> check, string w, bool gone, bool hasL, bool atL, bool at, uint deadline,
                               bool closing, int counter, EndCause expected)
        {
            EndCause got = SkiDayTiming.EndCauseOf(gone, hasL, atL, at, 1000000u, deadline, closing, counter);
            check(got == expected, w, $"EndCauseOf({gone}, {hasL}, {atL}, {at}, 1000000, {deadline}, {closing}, {counter}) = {got}, expected {expected}");
        }

        // ── ST18 ExitOfPurpose, TryExit, ExitWatchFrames, LateWatchFrames ─────────────────────────
        private static void ExitRows(Func<bool, string, string, bool> check)
        {
            const string w = "skiday-timing ST18";
            check(SkiDayTiming.ExitWayCount == 11 && (byte)ExitWay.Home == 0 && (byte)ExitWay.StillInside == 9 && (byte)ExitWay.Deleted == 10, w,
                  "ExitWay literals");
            check(SkiDayTiming.ExitOfPurpose(false, 3) == ExitWay.NoPurpose, w, "no purpose → NoPurpose");
            Ex(check, w, 3, ExitWay.Home);
            Ex(check, w, 4, ExitWay.WorkOrSchool);
            Ex(check, w, 10, ExitWay.WorkOrSchool);
            Ex(check, w, 1, ExitWay.Shopping);
            Ex(check, w, 2, ExitWay.AnotherOuting);
            Ex(check, w, 30, ExitWay.AnotherOuting);
            Ex(check, w, 31, ExitWay.AnotherOuting);
            Ex(check, w, 32, ExitWay.AnotherOuting);
            Ex(check, w, 33, ExitWay.AnotherOuting);
            Ex(check, w, 8, ExitWay.MovingAway);
            Ex(check, w, 12, ExitWay.Health);
            Ex(check, w, 24, ExitWay.Health);
            Ex(check, w, 25, ExitWay.Health);
            Ex(check, w, 26, ExitWay.Health);
            Ex(check, w, 38, ExitWay.PathFailed);
            Ex(check, w, 5, ExitWay.Other);
            Ex(check, w, 6, ExitWay.Other);
            Ex(check, w, 40, ExitWay.Other);
            const uint end = 1000000u;
            check(SkiDayTiming.TryExit(true, true, true, 3, end + 5u, end, out ExitWay a) && a == ExitWay.Deleted, w, "gone → Deleted");
            check(SkiDayTiming.TryExit(false, false, true, 3, end + 64u, end, out ExitWay b) && b == ExitWay.Home, w, "out of the lodge, GoingHome → Home");
            check(SkiDayTiming.TryExit(false, false, false, 0, end + 64u, end, out ExitWay c) && c == ExitWay.NoPurpose, w, "out of the lodge, no purpose → NoPurpose");
            check(!SkiDayTiming.TryExit(false, true, false, 0, end + 21844u, end, out ExitWay _), w, "inside at end + 21,844 → false (keep watching)");
            check(SkiDayTiming.TryExit(false, true, false, 0, end + 21845u, end, out ExitWay d) && d == ExitWay.StillInside, w, "inside at end + 21,845 → StillInside");
            check(SkiDayTiming.ExitWatchFrames == 21845u && SkiDayTiming.ExitWatchFrames == SkiDayTiming.FramesFor(2.0), w,
                  $"ExitWatchFrames {SkiDayTiming.ExitWatchFrames} = 21,845 = FramesFor(2) {SkiDayTiming.FramesFor(2.0)}");
            check(SkiDayTiming.LateWatchFrames == 65536u && SkiDayTiming.LateWatchFrames == SkiDayTiming.FramesFor(6.0), w,
                  $"LateWatchFrames {SkiDayTiming.LateWatchFrames} = 65,536 = FramesFor(6) {SkiDayTiming.FramesFor(6.0)}");
        }

        private static void Ex(Func<bool, string, string, bool> check, string w, byte purpose, ExitWay expected)
        {
            ExitWay got = SkiDayTiming.ExitOfPurpose(true, purpose);
            check(got == expected, w, $"ExitOfPurpose(true, {purpose}) = {got}, expected {expected}");
        }

        // ── ST19 MinuteOfDay, ClockMinuteAfter ────────────────────────────────────────────────────
        private static void ClockRows(Func<bool, string, string, bool> check)
        {
            const string w = "skiday-timing ST19";
            check(SkiDayTiming.MinuteOfDay(0.25f) == 360, w, "MinuteOfDay(0.25) = 360 (" + SkiDayTiming.MinuteOfDay(0.25f) + ")");
            check(SkiDayTiming.MinuteOfDay(0f) == 0, w, "MinuteOfDay(0) = 0");
            check(SkiDayTiming.MinuteOfDay(0.999999f) == 1439, w, "MinuteOfDay(0.999999) = 1439 (" + SkiDayTiming.MinuteOfDay(0.999999f) + ")");
            check(SkiDayTiming.MinuteOfDay(float.NaN) == 0, w, "MinuteOfDay(NaN) = 0");
            Cm(check, w, 300, 0u, 10923u, 360);          // 10,923 × 45 / 8192 = 60.0
            Cm(check, w, 1380, 0u, 21846u, 60);          // 23:00 + 2 h wraps to 01:00
            Cm(check, w, 0, 5u, 5u, 0);
            Cm(check, w, 600, 4294967000u, 10627u, 660); // the frame counter wraps: 10,923 frames later
        }

        private static void Cm(Func<bool, string, string, bool> check, string w, int start, uint startFrame, uint frame, int expected)
        {
            int got = SkiDayTiming.ClockMinuteAfter(start, startFrame, frame);
            check(got == expected, w, $"ClockMinuteAfter({start}, {startFrame}, {frame}) = {got}, expected {expected}");
        }

        // ── ST20 GuestScanSaw (pass 64, scans every 256 at 1000) ──────────────────────────────────
        private static void ScanRows(Func<bool, string, string, bool> check)
        {
            const string w = "skiday-timing ST20";
            check((byte)ScanVerdict.Seen == 0 && (byte)ScanVerdict.Missed == 1 && (byte)ScanVerdict.Unsure == 2, w, "ScanVerdict literals");
            Sc(check, w, 1024u, 1280u, 1344u, true, 1000u, 256u, ScanVerdict.Seen);     // 1256 lies strictly inside
            Sc(check, w, 1088u, 1088u, 1152u, true, 1000u, 256u, ScanVerdict.Missed);   // no scan in (1024, 1152)
            Sc(check, w, 1024u, 1024u, 1088u, true, 1000u, 256u, ScanVerdict.Unsure);   // 1000 lies after 960
            Sc(check, w, 1024u, 1280u, 1344u, false, 1000u, 256u, ScanVerdict.Unsure);  // scanKnown false
            Sc(check, w, 1024u, 1280u, 1344u, true, 1000u, 0u, ScanVerdict.Unsure);     // scanInterval 0
            Sc(check, w, 1000u, 1100u, 1164u, true, 1000u, 256u, ScanVerdict.Unsure);   // a scan exactly at first is not Seen
            Sc(check, w, 1100u, 1256u, 1320u, true, 1000u, 256u, ScanVerdict.Unsure);   // nor exactly at last
            Sc(check, w, 1024u, 1300u, 1364u, true, 4294967040u, 256u, ScanVerdict.Seen);   // scan frames wrap: 1280 inside
        }

        private static void Sc(Func<bool, string, string, bool> check, string w, uint first, uint last, uint end, bool known, uint scan,
                               uint interval, ScanVerdict expected)
        {
            ScanVerdict got = SkiDayTiming.GuestScanSaw(first, last, end, 64u, known, scan, interval);
            check(got == expected, w, $"GuestScanSaw({first}, {last}, {end}, 64, {known}, {scan}, {interval}) = {got}, expected {expected}");
        }

        // ── ST21 CarReserveOf, all eight (departed, heldEver, heldAtHome) ─────────────────────────
        private static void ReserveRows(Func<bool, string, string, bool> check)
        {
            const string w = "skiday-timing ST21";
            Cr(check, w, false, false, false, CarReserve.NeverHeldNeverLeft);
            Cr(check, w, false, false, true, CarReserve.HeldNeverLeft);    // heldAtHome reads as held
            Cr(check, w, false, true, false, CarReserve.HeldNeverLeft);
            Cr(check, w, false, true, true, CarReserve.HeldNeverLeft);
            Cr(check, w, true, false, false, CarReserve.NeverHeldLeft);
            Cr(check, w, true, false, true, CarReserve.HeldAtHome);        // heldAtHome reads as held
            Cr(check, w, true, true, false, CarReserve.HeldAfterLeaving);
            Cr(check, w, true, true, true, CarReserve.HeldAtHome);
            check(SkiDayTiming.CarReserveCount == 5 && (byte)CarReserve.NeverHeldNeverLeft == 4, w, "CarReserve literals 0..4");
        }

        private static void Cr(Func<bool, string, string, bool> check, string w, bool dep, bool ever, bool home, CarReserve expected)
        {
            CarReserve got = SkiDayTiming.CarReserveOf(dep, ever, home);
            check(got == expected, w, $"CarReserveOf({dep}, {ever}, {home}) = {got}, expected {expected}");
        }

        // ── ST22 ClockBin, StayBin, ClosestBand ───────────────────────────────────────────────────
        private static void BinRows(Func<bool, string, string, bool> check)
        {
            const string w = "skiday-timing ST22";
            float[] t = { 0f, 0.0833f, 0.0834f, 0.5f, 0.99f, float.NaN };
            int[] tb = { 0, 0, 1, 6, 11, 0 };
            for (int i = 0; i < t.Length; i++)
                check(SkiDayTiming.ClockBin(t[i]) == tb[i], w, $"ClockBin({t[i]:R}) = {SkiDayTiming.ClockBin(t[i])}, expected {tb[i]}");
            double[] h = { 0, 0.49, 0.5, 1.99, 2, 4, 6, 30, double.NaN, -1 };
            int[] hb = { 0, 0, 1, 2, 3, 4, 5, 5, 0, 0 };
            for (int i = 0; i < h.Length; i++)
                check(SkiDayTiming.StayBin(h[i]) == hb[i], w, $"StayBin({h[i]:R}) = {SkiDayTiming.StayBin(h[i])}, expected {hb[i]}");
            float[] m = { 0f, 499f, 500f, 1999f, 2000f, float.PositiveInfinity, float.NaN };
            int[] mb = { 0, 0, 1, 1, 2, 3, 3 };
            for (int i = 0; i < m.Length; i++)
                check(SkiDayTiming.ClosestBand(m[i]) == mb[i], w, $"ClosestBand({m[i]:R}) = {SkiDayTiming.ClosestBand(m[i])}, expected {mb[i]}");
        }

        // ── ST23 MarkHoursPerMonth (12 days: 288 hours, month = hour / 24) ────────────────────────
        private static void MarkHourRows(Func<bool, string, string, bool> check)
        {
            const string w = "skiday-timing ST23";
            var marks = new int[12];
            var snows = new int[12];
            var snow = new bool[288];
            for (int hr = 0; hr < 24; hr++)
                snow[hr] = true;   // every hour of month 0
            SkiDayTiming.MarkHoursPerMonth(snow, 12, marks, snows);
            check(marks[0] == 18 && marks[1] == 17 && Sum(marks, 2, 10) == 0 && snows[0] == 24 && Sum(snows, 1, 11) == 0, w,
                  $"snow in month 0 only: marks {marks[0]}/{marks[1]}/rest {Sum(marks, 2, 10)}, snow-hours {snows[0]}/rest {Sum(snows, 1, 11)}; expected 18/17/0, 24/0");
            Array.Clear(snow, 0, snow.Length);
            for (int hr = 264; hr < 288; hr++)
                snow[hr] = true;   // every hour of month 11
            SkiDayTiming.MarkHoursPerMonth(snow, 12, marks, snows);
            check(marks[11] == 18 && marks[0] == 17 && Sum(marks, 1, 10) == 0 && snows[11] == 24, w,
                  $"snow in month 11 only (the wrap): marks Dec {marks[11]}, Jan {marks[0]}, rest {Sum(marks, 1, 10)}; expected 18, 17, 0");
            Array.Clear(snow, 0, snow.Length);
            SkiDayTiming.MarkHoursPerMonth(snow, 12, marks, snows);
            check(Sum(marks, 0, 12) == 0 && Sum(snows, 0, 12) == 0, w, "no snow → all 0 (the outputs are cleared first)");
            for (int hr = 0; hr < 288; hr++)
                snow[hr] = true;
            SkiDayTiming.MarkHoursPerMonth(snow, 12, marks, snows);
            bool all24 = true;
            for (int i = 0; i < 12; i++)
                all24 &= marks[i] == 24 && snows[i] == 24;
            check(all24, w, "snow everywhere → 24 marks and 24 snow-hours each month");
            // the helpers it rests on (SkiDayRules): MonthOfHour at 12 days
            check(SkiDayRules.MonthOfHour(23, 12) == 0 && SkiDayRules.MonthOfHour(24, 12) == 1 && SkiDayRules.MonthOfHour(287, 12) == 11, w,
                  "MonthOfHour(23 / 24 / 287, 12) = 0 / 1 / 11");
        }

        private static int Sum(int[] a, int start, int count)
        {
            int s = 0;
            for (int i = start; i < start + count; i++)
                s += a[i];
            return s;
        }

        // ── ST24 WhyOf(gone, ill, movingAway, hasPurpose, purpose, hasTrip, trip, resourceBuyer) ──
        private static void WhyRows(Func<bool, string, string, bool> check)
        {
            const string w = "skiday-timing ST24";
            check(SkiDayTiming.GoneWhyCount == 9 && (byte)GoneWhy.Unknown == 0 && (byte)GoneWhy.Deleted == 8, w, "GoneWhy literals 0..8");
            Wy(check, w, true, true, true, true, 6, true, 4, true, GoneWhy.Deleted);         // gone beats all
            Wy(check, w, false, true, true, true, 6, false, 0, false, GoneWhy.Ill);          // ill beats moving away
            Wy(check, w, false, false, true, true, 6, true, 4, true, GoneWhy.MovingAway);    // moving away beats a purpose
            Wy(check, w, false, false, false, true, 6, false, 0, false, GoneWhy.Bedtime);
            Wy(check, w, false, false, false, true, 4, false, 0, false, GoneWhy.WorkOrSchool);
            Wy(check, w, false, false, false, true, 5, false, 0, false, GoneWhy.WorkOrSchool);
            Wy(check, w, false, false, false, true, 9, false, 0, false, GoneWhy.WorkOrSchool);
            Wy(check, w, false, false, false, true, 10, false, 0, false, GoneWhy.WorkOrSchool);
            Wy(check, w, false, false, false, false, 0, true, 4, false, GoneWhy.WorkOrSchool);
            Wy(check, w, false, false, false, false, 0, true, 10, false, GoneWhy.WorkOrSchool);
            Wy(check, w, false, false, false, false, 0, false, 0, true, GoneWhy.Shopping);
            Wy(check, w, false, false, false, true, 1, false, 0, false, GoneWhy.Shopping);
            Wy(check, w, false, false, false, false, 0, true, 1, false, GoneWhy.Shopping);
            Wy(check, w, false, false, false, true, 3, false, 0, false, GoneWhy.HeadingHome);
            Wy(check, w, false, false, false, false, 0, true, 3, false, GoneWhy.HeadingHome);
            Wy(check, w, false, false, false, true, 30, false, 0, false, GoneWhy.OtherTrip);
            Wy(check, w, false, false, false, true, 2, true, 2, false, GoneWhy.Unknown);     // Leisure purpose and trip only
            Wy(check, w, false, false, false, false, 0, false, 0, false, GoneWhy.Unknown);   // nothing
            Wy(check, w, false, false, false, true, 6, true, 4, false, GoneWhy.Bedtime);     // bedtime beats a work trip
        }

        private static void Wy(Func<bool, string, string, bool> check, string w, bool gone, bool ill, bool moving, bool hasP, byte p, bool hasT,
                               byte trip, bool buyer, GoneWhy expected)
        {
            GoneWhy got = SkiDayTiming.WhyOf(gone, ill, moving, hasP, p, hasT, trip, buyer);
            check(got == expected, w, $"WhyOf({gone}, {ill}, {moving}, {hasP}, {p}, {hasT}, {trip}, {buyer}) = {got}, expected {expected}");
        }

        // ── ST25 TryLate (claim deadline 1,000,000) and HasMarker ─────────────────────────────────
        private static void LateRows(Func<bool, string, string, bool> check)
        {
            const string w = "skiday-timing ST25";
            const uint dl = 1000000u;
            check((byte)LateResult.ArrivedLate == 0 && (byte)LateResult.NotWithin6h == 3 && SkiDayTiming.LateResultCount == 4, w, "LateResult literals");
            Lt(check, w, true, true, true, dl, dl + 100u, true, LateResult.Deleted);
            Lt(check, w, false, true, false, 0u, dl + 100u, true, LateResult.ArrivedLate);       // at the lodge without Leisure
            Lt(check, w, false, true, true, dl, dl + 100u, true, LateResult.ArrivedLate);        // with Leisure of the claim's deadline
            Lt(check, w, false, true, true, dl + 5000u, dl + 100u, true, LateResult.GaveUp);     // another deadline, at the lodge
            Lt(check, w, false, false, true, dl + 5000u, dl + 100u, true, LateResult.GaveUp);    // another deadline, not there
            Lt(check, w, false, false, false, 0u, dl + 100u, true, LateResult.GaveUp);           // no Leisure, not there
            Lt(check, w, false, false, true, dl, dl + 65535u, false, LateResult.NotWithin6h);    // on the way: keep watching
            Lt(check, w, false, false, true, dl, dl + 65536u, true, LateResult.NotWithin6h);

            const string wm = "skiday-timing ST25 markers";
            check(SkiDayTiming.MarkerCount == 5, wm, "MarkerCount 5");
            check(SkiDayTiming.FlagDeparted == 1 && SkiDayTiming.FlagHeldAtHome == 2 && SkiDayTiming.FlagHeldEver == 4 && SkiDayTiming.FlagRode == 8 &&
                  SkiDayTiming.FlagPathFailedAtStart == 16 && SkiDayTiming.FlagDeadlineIsClosing == 32 && SkiDayTiming.FlagLateArrived == 64 &&
                  SkiDayTiming.Flag2TurnedBack == 1 && SkiDayTiming.Flag2WalkerLost == 2 && SkiDayTiming.Flag2WalkerPathFailed == 4 &&
                  SkiDayTiming.Flag2VehiclePathFailed == 8, wm, "flag bit literals (m1-spec §1.1)");
            Mk(check, wm, SkiDayTiming.MarkerPathFailedAtStart, SkiDayTiming.FlagPathFailedAtStart, 0);
            Mk(check, wm, SkiDayTiming.MarkerWalkerPathFailed, 0, SkiDayTiming.Flag2WalkerPathFailed);
            Mk(check, wm, SkiDayTiming.MarkerVehiclePathFailed, 0, SkiDayTiming.Flag2VehiclePathFailed);
            Mk(check, wm, SkiDayTiming.MarkerWalkerLost, 0, SkiDayTiming.Flag2WalkerLost);
            Mk(check, wm, SkiDayTiming.MarkerTurnedBack, 0, SkiDayTiming.Flag2TurnedBack);
            check(!SkiDayTiming.HasMarker(255, 255, 5) && !SkiDayTiming.HasMarker(255, 255, -1), wm, "an unknown marker is never set");
            // every other flag bit leaves a marker unset
            byte otherFlags = (byte)(255 & ~SkiDayTiming.FlagPathFailedAtStart);
            byte otherFlags2 = (byte)(255 & ~(SkiDayTiming.Flag2TurnedBack | SkiDayTiming.Flag2WalkerLost | SkiDayTiming.Flag2WalkerPathFailed |
                                              SkiDayTiming.Flag2VehiclePathFailed));
            int set = 0;
            for (int k = 0; k < SkiDayTiming.MarkerCount; k++)
            {
                if (SkiDayTiming.HasMarker(otherFlags, otherFlags2, k))
                    set++;
            }
            check(set == 0, wm, $"no marker from the other bits: {set} set");
        }

        // ── ST26 DepartedNow(inBuilding, atOrigin, atLodge, hasPurpose, purpose) (fixer M1-ECS-2) ────────
        private static void DepartRows(Func<bool, string, string, bool> check)
        {
            const string w = "skiday-timing ST26";
            // in the origin: never departed, whatever the purpose
            Dp(check, w, true, true, false, false, 0, false);
            Dp(check, w, true, true, false, true, SkiDayTiming.PurposeLeisure, false);
            Dp(check, w, true, true, false, true, SkiDayTiming.PurposeGoingHome, false);
            // on the road (no building): departed with Leisure, with GoingHome (a car guest turned home before our pass:
            // the M1-ECS-2 case), and with no TravelPurpose at all
            Dp(check, w, false, false, false, true, SkiDayTiming.PurposeLeisure, true);
            Dp(check, w, false, false, false, true, SkiDayTiming.PurposeGoingHome, true);
            Dp(check, w, false, false, false, false, 0, true);
            // in the lodge: departed
            Dp(check, w, true, false, true, false, 0, true);
            // in another building: only with TravelPurpose Leisure
            Dp(check, w, true, false, false, true, SkiDayTiming.PurposeLeisure, true);
            Dp(check, w, true, false, false, true, SkiDayTiming.PurposeShopping, false);
            Dp(check, w, true, false, false, false, 0, false);
        }

        private static void Dp(Func<bool, string, string, bool> check, string w, bool inB, bool atO, bool atL, bool hasP, byte p, bool expected)
        {
            bool got = SkiDayTiming.DepartedNow(inB, atO, atL, hasP, p);
            check(got == expected, w, $"DepartedNow({inB}, {atO}, {atL}, {hasP}, {p}) = {got}, expected {expected}");
        }

        private static void Lt(Func<bool, string, string, bool> check, string w, bool gone, bool at, bool hasL, uint leisureDl, uint now, bool expectDone,
                               LateResult expected)
        {
            bool done = SkiDayTiming.TryLate(gone, at, hasL, leisureDl, 1000000u, now, out LateResult got);
            check(done == expectDone && (!done || got == expected), w,
                  $"TryLate({gone}, {at}, {hasL}, {leisureDl}, 1000000, {now}) = ({done}, {got}), expected ({expectDone}, {expected})");
        }

        private static void Mk(Func<bool, string, string, bool> check, string w, int marker, byte flags, byte flags2)
        {
            check(SkiDayTiming.HasMarker(flags, flags2, marker), w, $"HasMarker({flags}, {flags2}, {marker}) is set");
            check(!SkiDayTiming.HasMarker(0, 0, marker), w, $"HasMarker(0, 0, {marker}) is not set");
        }
    }
}
