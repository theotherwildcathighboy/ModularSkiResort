// Tests for the pure SkiDayTiming (tests/ResortBatch): `public static class SkiDayTimingTests` with
// `public static void Run(Func<bool, string, string, bool> check)` and its own Near; Program.Main calls SkiDayTimingTests.Run(Check).
// Oracles are literal hand values. Row groups are tagged ST0..ST46 in the check names.
//
// Covers: the constants and enum literals (ST0); RecentBehaviorSlice (ST1, ST1b: CitizenBehaviorSystem at interval 16, offset 11);
// SkipChunk (ST2: LeisureSystem 64 + the recent CBS slice); IsLastPass (ST3); FramesFor / HoursFor (ST4, ST4b: the uint.MaxValue
// clamp); Resolve (ST5: every on-the-way and on-site row); HourStep (ST6); NextLeisureVisit (ST7: the resident roll key); TrackIndex
// (ST8: the job's index guard); SameBits (ST9). Property sweeps (ST10-ST13) tie the helpers to each other and to
// SkiDayRules.SliceOf / SkipSlice (one decision = one NextLeisureVisit; IsLastPass iff next visit is one interval away; the
// recent-CBS window is exactly the 8 frames from a run; SkipChunk contains SkipSlice). IsLastSighting rows (ST14) and the sweep
// over all 64 LeisureSystem offsets (ST15). Then ST16 PlaceOf, ST17 EndCauseOf (EndCause literals 0..8, EndCauseCount 9), ST18
// ExitOfPurpose / TryExit and the two watch constants, ST19 MinuteOfDay / ClockMinuteAfter, ST20 GuestScanSaw, ST21 CarReserveOf,
// ST22 ClockBin / StayBin / ClosestBand, ST23 MarkHoursPerMonth, ST24 WhyOf, ST25 TryLate and HasMarker, ST26 DepartedNow (a guest
// on the road with GoingHome has departed), ST27 ArrivedThenRemoved, ST28 the on-arrival visit end (stay 0, scan verdict never
// Seen), ST29 MovedOffNow, ST30 ShowNear500, ST31-ST33 remover-reason (constants, ShiftNeed / NeedTaken / IsFastStay, RemoveWhyOf
// rows and sweeps, NoPurposeAtOf), ST34-ST42 the follow and walkers near a lodge (PlacedNow, TryFollow without a previous penalty),
// ST43-ST44 FailureEvidence / LateEvidence (one predicate), ST45-ST46 the deadline pull (with the 21:00 flap guard).
// Not testable headless (ECS): the jobs, the claim itself, the car-reserve handshake, ResolveJob sampling and the ring, NearJob, the
// measure rows, the ledger check, the texts.

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
using RemoveWhy = SkiResortMod.Systems.SkiDayTiming.RemoveWhy;
using NoPurposeAt = SkiResortMod.Systems.SkiDayTiming.NoPurposeAt;
using FollowResult = SkiResortMod.Systems.SkiDayTiming.FollowResult;
using StillOut = SkiResortMod.Systems.SkiDayTiming.StillOut;
using WalkerKind = SkiResortMod.Systems.SkiDayTiming.WalkerKind;
using SpeedEvidence = SkiResortMod.Systems.SkiDayTiming.SpeedEvidence;

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
            LastSighting(check);
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
            DepartRows(check);     // ST26
            OnArrivalRows(check);      // ST27
            OnArrivalEndRows(check);   // ST28
            MovedOffRows(check);       // ST29
            Near500Rows(check);        // ST30
            RemoveConstRows(check);    // ST31
            RemoveWhyRows(check);      // ST32
            NoPurposeRows(check);      // ST33
            FollowConstRows(check);    // ST34
            FollowKindRows(check);     // ST35
            PlacedRows(check);         // ST36
            TryFollowRows(check);      // ST37
            StillOutRows(check);       // ST38
            WaitHourRows(check);       // ST39
            WalkerKindRows(check);     // ST40
            WithinRows(check);         // ST41
            SlotRows(check);           // ST42
            FailureEvidenceRows(check);   // ST43
            LateEvidenceRows(check);   // ST44
            PullConstRows(check);      // ST45
            PullRows(check);           // ST46
        }

        private static bool Near(double a, double b, double eps)
        {
            return Math.Abs(a - b) <= eps;
        }

        // ST0 constants
        private static void Constants(Func<bool, string, string, bool> check)
        {
            const string w = "skiday-timing ST0";
            check(SkiDayTiming.MaxFramesPerRenderFrame == 8, w, "MaxFramesPerRenderFrame " + SkiDayTiming.MaxFramesPerRenderFrame);
            check(SkiDayTiming.OnSiteGraceFrames == 21845u, w, "OnSiteGraceFrames " + SkiDayTiming.OnSiteGraceFrames);
            check(SkiDayTiming.OnSiteGraceFrames == SkiDayTiming.FramesFor(2.0), w, "OnSiteGraceFrames = FramesFor(2)");
            check((byte)Outcome.OnTheWay == 0 && (byte)Outcome.Arrived == 1 && (byte)Outcome.OnSite == 2 && (byte)Outcome.Left == 3 &&
                  (byte)Outcome.Gone == 4 && (byte)Outcome.Retargeted == 5 && (byte)Outcome.Expired == 6, w, "ClaimOutcome literals 0..6");
        }

        // ST1, ST1b RecentBehaviorSlice(F, 16, 11)
        private static void RecentBehavior(Func<bool, string, string, bool> check)
        {
            const string w = "skiday-timing ST1";
            Recent(check, w, 1000000u, 16, 11, true, 3u);    // (1,000,000 − 11) mod 16 = 5 → last 999,995; 62,499 & 15 = 3
            Recent(check, w, 1000011u, 16, 11, true, 4u);    // a run at F: diff 0; 62,500 & 15 = 4
            Recent(check, w, 1000018u, 16, 11, true, 4u);    // last 1,000,011, diff 7
            Recent(check, w, 1000019u, 16, 11, false, 0u);   // diff 8 is not < 8
            Recent(check, w, 1000064u, 16, 11, true, 7u);    // last 1,000,059; 62,503 & 15 = 7
            Recent(check, w, 5u, 16, 11, false, 0u);         // 5 < offset 11: no run yet
            Recent(check, "skiday-timing ST1b", 1000u, 0, 11, false, 0u);    // interval guard
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

        // ST2 SkipChunk, ST3 IsLastPass
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

        // ST4, ST4b FramesFor / HoursFor
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

        // ST5 Resolve, deadline 1,075,366
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

        // ST6 HourStep
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

        // ST7 NextLeisureVisit (LS 64, slice 12)
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

        // ST8 TrackIndex
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

        // ST9 SameBits
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

        // ST10-ST13 property sweeps (tie the helpers to each other and to SkiDayRules)
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

        // ST14, ST15 IsLastSighting (CBS 16 / 11, LeisureSystem 64)
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
            // enters a pool once); the last sighting is not the last pass at 32 offsets (q 0-2, 11-18, 27-34, 43-50, 59-63).
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


        // ST16 PlaceOf(gone, inBuilding, atOrigin, atLodge, departed)
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

        // ST17 EndCauseOf (now 1,000,000, deadline 1,000,100)
        private static void EndCauseRows(Func<bool, string, string, bool> check)
        {
            const string w = "skiday-timing ST17";
            check((byte)EndCause.DeadlineOwn == 0 && (byte)EndCause.DeadlineClosing == 1 && (byte)EndCause.NeedFilled == 2 &&
                  (byte)EndCause.NewOuting == 3 && (byte)EndCause.Removed == 4 && (byte)EndCause.WalkedOut == 5 &&
                  (byte)EndCause.PastGrace == 6 && (byte)EndCause.Deleted == 7 && (byte)EndCause.RemovedOnArrival == 8 &&
                  SkiDayTiming.EndCauseCount == 9, w, "EndCause literals 0..8");
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

        // ST18 ExitOfPurpose, TryExit, ExitWatchFrames, LateWatchFrames
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

        // ST19 MinuteOfDay, ClockMinuteAfter
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

        // ST20 GuestScanSaw (pass 64, scans every 256 at 1000)
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

        // ST21 CarReserveOf, all eight (departed, heldEver, heldAtHome)
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

        // ST22 ClockBin, StayBin, ClosestBand
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

        // ST23 MarkHoursPerMonth (12 days: 288 hours, month = hour / 24)
        private static void MarkHourRows(Func<bool, string, string, bool> check)
        {
            const string w = "skiday-timing ST23";
            var marks = new int[12];
            var snows = new int[12];
            var snow = new bool[288];
            var fresh = new float[288];   // the fresh snow per hour; 0.65 on a snow hour, so 7 snow hours = 4.55 = the mark (6 = 3.9 is not)
            void Fill() { for (int i = 0; i < 288; i++) fresh[i] = snow[i] ? 0.65f : 0f; }
            for (int hr = 0; hr < 24; hr++)
                snow[hr] = true;   // every hour of month 0
            Fill();
            SkiDayTiming.MarkHoursPerMonth(snow, fresh, 12, marks, snows);
            check(marks[0] == 18 && marks[1] == 17 && Sum(marks, 2, 10) == 0 && snows[0] == 24 && Sum(snows, 1, 11) == 0, w,
                  $"snow in month 0 only: marks {marks[0]}/{marks[1]}/rest {Sum(marks, 2, 10)}, snow-hours {snows[0]}/rest {Sum(snows, 1, 11)}; expected 18/17/0, 24/0");
            Array.Clear(snow, 0, snow.Length);
            for (int hr = 264; hr < 288; hr++)
                snow[hr] = true;   // every hour of month 11
            Fill();
            SkiDayTiming.MarkHoursPerMonth(snow, fresh, 12, marks, snows);
            check(marks[11] == 18 && marks[0] == 17 && Sum(marks, 1, 10) == 0 && snows[11] == 24, w,
                  $"snow in month 11 only (the wrap): marks Dec {marks[11]}, Jan {marks[0]}, rest {Sum(marks, 1, 10)}; expected 18, 17, 0");
            Array.Clear(snow, 0, snow.Length);
            Fill();
            SkiDayTiming.MarkHoursPerMonth(snow, fresh, 12, marks, snows);
            check(Sum(marks, 0, 12) == 0 && Sum(snows, 0, 12) == 0, w, "no snow → all 0 (the outputs are cleared first)");
            for (int hr = 0; hr < 288; hr++)
                snow[hr] = true;
            Fill();
            SkiDayTiming.MarkHoursPerMonth(snow, fresh, 12, marks, snows);
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

        // ST24 WhyOf(gone, ill, movingAway, hasPurpose, purpose, hasTrip, trip, resourceBuyer)
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

        // ST25 TryLate (claim deadline 1,000,000) and HasMarker
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

        // ST26 DepartedNow(inBuilding, atOrigin, atLodge, hasPurpose, purpose)
        private static void DepartRows(Func<bool, string, string, bool> check)
        {
            const string w = "skiday-timing ST26";
            // in the origin: never departed, whatever the purpose
            Dp(check, w, true, true, false, false, 0, false);
            Dp(check, w, true, true, false, true, SkiDayTiming.PurposeLeisure, false);
            Dp(check, w, true, true, false, true, SkiDayTiming.PurposeGoingHome, false);
            // on the road (no building): departed with Leisure, with GoingHome (a car guest turned home before our pass:
            // the GoingHome case), and with no TravelPurpose at all
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


        // ST27 ArrivedThenRemoved(outcome, gone, atLodge): 9 rows + 2 chained with Resolve (11)
        private static void OnArrivalRows(Func<bool, string, string, bool> check)
        {
            const string w = "skiday-timing ST27";
            Atr(check, w, Outcome.Gone, false, true, true);
            Atr(check, w, Outcome.Gone, true, true, false);         // deleted citizen: a failure, place "deleted"
            Atr(check, w, Outcome.Gone, false, false, false);
            Atr(check, w, Outcome.Retargeted, false, true, false);
            Atr(check, w, Outcome.Expired, false, true, false);     // the late watch books it
            Atr(check, w, Outcome.Arrived, false, true, false);
            Atr(check, w, Outcome.OnTheWay, false, true, false);
            Atr(check, w, Outcome.OnSite, false, true, false);
            Atr(check, w, Outcome.Left, false, true, false);
            // chained with ST5's deadline 1,075,366: on the way, no Leisure, in the lodge
            Outcome g = SkiDayTiming.Resolve(false, false, false, true, 1050000u, 1075366u);
            check(g == Outcome.Gone && SkiDayTiming.ArrivedThenRemoved(g, false, true), w,
                  $"Resolve(false, false, false, true, 1050000, 1075366) = {g} (expected Gone) and ArrivedThenRemoved true");
            Outcome e = SkiDayTiming.Resolve(false, false, false, true, 1080000u, 1075366u);
            check(e == Outcome.Expired && !SkiDayTiming.ArrivedThenRemoved(e, false, true), w,
                  $"Resolve(false, false, false, true, 1080000, 1075366) = {e} (expected Expired: the deadline first) and ArrivedThenRemoved false");
        }

        private static void Atr(Func<bool, string, string, bool> check, string w, Outcome o, bool gone, bool atLodge, bool expected)
        {
            bool got = SkiDayTiming.ArrivedThenRemoved(o, gone, atLodge);
            check(got == expected, w, $"ArrivedThenRemoved({o}, {gone}, {atLodge}) = {got}, expected {expected}");
        }

        // ST28 the on-arrival visit end: stay 0 and the scan verdict (pass 64, scans every 256) (6)
        private static void OnArrivalEndRows(Func<bool, string, string, bool> check)
        {
            const string w = "skiday-timing ST28";
            check(SkiDayTiming.HoursFor(0u) == 0.0 && SkiDayTiming.StayBin(0.0) == 0, w,
                  $"stay HoursFor(0) = {SkiDayTiming.HoursFor(0u)} in StayBin {SkiDayTiming.StayBin(0.0)}, expected 0.0 in bin 0 (0-0.5 h)");
            // arrival = this pass 1088, last there = the pass before 1024 (first > last: never Seen), end 1088
            Sc(check, w, 1088u, 1024u, 1088u, true, 1000u, 256u, ScanVerdict.Missed);   // no scan in (1024, 1088)
            Sc(check, w, 1088u, 1024u, 1088u, true, 1050u, 256u, ScanVerdict.Unsure);   // a scan inside the pass window
            Sc(check, w, 1088u, 1024u, 1088u, true, 1087u, 256u, ScanVerdict.Unsure);
            Sc(check, w, 1088u, 1024u, 1088u, true, 1024u, 256u, ScanVerdict.Missed);   // a scan at the previous pass is not inside
            Sc(check, w, 1088u, 1024u, 1088u, true, 1088u, 256u, ScanVerdict.Missed);   // nor one at this pass
        }

        // ST29 MovedOffNow(departed, inBuilding, walkerLive, mark) (6)
        private static void MovedOffRows(Func<bool, string, string, bool> check)
        {
            const string w = "skiday-timing ST29";
            Mv(check, w, false, false, true, false, false);   // not departed
            Mv(check, w, true, true, true, false, false);     // in a building: no walker read
            Mv(check, w, true, false, false, false, false);   // walker lost
            Mv(check, w, true, false, true, true, false);     // waiting at the door (Divert PathFailed)
            Mv(check, w, true, false, true, false, true);
            Mv(check, w, false, true, false, true, false);
        }

        private static void Mv(Func<bool, string, string, bool> check, string w, bool dep, bool inB, bool live, bool mark, bool expected)
        {
            bool got = SkiDayTiming.MovedOffNow(dep, inB, live, mark);
            check(got == expected, w, $"MovedOffNow({dep}, {inB}, {live}, {mark}) = {got}, expected {expected}");
        }

        // ST30 ShowNear500(distanceM, near500Frame) (6)
        private static void Near500Rows(Func<bool, string, string, bool> check)
        {
            const string w = "skiday-timing ST30";
            N5(check, w, 100f, 5u, false);
            N5(check, w, 500f, 5u, false);
            N5(check, w, 500.5f, 5u, true);
            N5(check, w, 3400f, 0u, false);    // never within 500 m
            N5(check, w, 3400f, 1u, true);
            N5(check, w, float.NaN, 5u, false);
        }

        private static void N5(Func<bool, string, string, bool> check, string w, float d, uint frame, bool expected)
        {
            bool got = SkiDayTiming.ShowNear500(d, frame);
            check(got == expected, w, $"ShowNear500({d:R}, {frame}) = {got}, expected {expected}");
        }

        // ST31 remover constants, ShiftNeed, NeedTaken, IsFastStay (24)
        private static void RemoveConstRows(Func<bool, string, string, bool> check)
        {
            const string w = "skiday-timing ST31";
            check(SkiDayTiming.RemoveWhyCount == 10 && (byte)RemoveWhy.Unknown == 0 && (byte)RemoveWhy.MoneyCheck == 1 &&
                  (byte)RemoveWhy.MovingAway == 2 && (byte)RemoveWhy.Ill == 3 && (byte)RemoveWhy.WorkTime == 4 && (byte)RemoveWhy.Bedtime == 5 &&
                  (byte)RemoveWhy.Shopping == 6 && (byte)RemoveWhy.VenueOff == 7 && (byte)RemoveWhy.NeedMet == 8 && (byte)RemoveWhy.NoPurpose == 9,
                  w, "RemoveWhy literals 0..9, RemoveWhyCount 10");
            check(SkiDayTiming.NoPurposeAtCount == 6 && (byte)NoPurposeAt.PlacedHome == 0 && (byte)NoPurposeAt.Home == 1 &&
                  (byte)NoPurposeAt.MapEdge == 2 && (byte)NoPurposeAt.PlacedElsewhere == 3 && (byte)NoPurposeAt.Elsewhere == 4 &&
                  (byte)NoPurposeAt.NoBuilding == 5, w, "NoPurposeAt literals 0..5, NoPurposeAtCount 6");
            check(SkiDayTiming.PlacedMarkMin == 250 && SkiDayTiming.FastStayHours == 0.2 && SkiDayTiming.Flag2NeedOpen == 16 &&
                  SkiDayTiming.Flag2NeedOpenBefore == 32, w,
                  $"PlacedMarkMin {SkiDayTiming.PlacedMarkMin} = 250, FastStayHours {SkiDayTiming.FastStayHours:R} = 0.2, need bits {SkiDayTiming.Flag2NeedOpen} / {SkiDayTiming.Flag2NeedOpenBefore} = 16 / 32");
            Sn(check, w, 0, true, 16);
            Sn(check, w, 16, true, 48);
            Sn(check, w, 16, false, 32);
            Sn(check, w, 32, false, 0);
            Sn(check, w, 48, false, 32);
            Sn(check, w, 15, false, 15);    // the marker bits are kept
            Sn(check, w, 63, false, 47);
            Sn(check, w, 80, false, 96);    // the pull flag 64 is kept
            Nt(check, w, 16, false, true);
            Nt(check, w, 32, false, true);
            Nt(check, w, 48, false, true);
            Nt(check, w, 48, true, false);  // still open: not taken
            Nt(check, w, 15, false, false);
            Nt(check, w, 0, false, false);
            Nt(check, w, 64, false, false); // the pull flag is not the register
            Fs(check, w, 0.0, true);
            Fs(check, w, 0.1999, true);
            Fs(check, w, 0.2, false);
            Fs(check, w, 5.0, false);
            Fs(check, w, -0.1, false);
            Fs(check, w, double.NaN, false);
        }

        private static void Sn(Func<bool, string, string, bool> check, string w, byte flags2, bool open, byte expected)
        {
            byte got = SkiDayTiming.ShiftNeed(flags2, open);
            check(got == expected, w, $"ShiftNeed({flags2}, {open}) = {got}, expected {expected}");
        }

        private static void Nt(Func<bool, string, string, bool> check, string w, byte flags2, bool open, bool expected)
        {
            bool got = SkiDayTiming.NeedTaken(flags2, open);
            check(got == expected, w, $"NeedTaken({flags2}, {open}) = {got}, expected {expected}");
        }

        private static void Fs(Func<bool, string, string, bool> check, string w, double hours, bool expected)
        {
            bool got = SkiDayTiming.IsFastStay(hours);
            check(got == expected, w, $"IsFastStay({hours:R}) = {got}, expected {expected}");
        }

        // ST32 RemoveWhyOf: 25 rows + 3 sweeps (one check each, violations counted) (28)
        private static void RemoveWhyRows(Func<bool, string, string, bool> check)
        {
            const string w = "skiday-timing ST32";
            const bool T = true, F = false;
            // (moving, money, ill, work, bed | hasP, p | hasT, t | buyer, need, venue | counter | at) → expected
            Rw(check, w, T, T, T, T, T, T, 1, T, 1, T, T, T, 255, F, RemoveWhy.MoneyCheck);
            Rw(check, w, T, F, T, T, T, T, 1, F, 0, T, T, T, 255, T, RemoveWhy.MovingAway);
            Rw(check, w, F, T, F, F, F, F, 0, F, 0, F, F, F, 0, T, RemoveWhy.Unknown);       // the money flag alone means nothing
            Rw(check, w, F, F, F, F, F, T, 8, F, 0, F, F, F, 0, F, RemoveWhy.MovingAway);    // purpose, household gone
            Rw(check, w, F, F, F, F, F, F, 0, T, 8, F, F, F, 0, T, RemoveWhy.MovingAway);    // trip
            Rw(check, w, F, F, T, T, T, F, 0, F, 0, T, T, T, 255, T, RemoveWhy.Ill);
            Rw(check, w, F, F, F, T, T, F, 0, F, 0, T, T, T, 255, T, RemoveWhy.WorkTime);
            Rw(check, w, F, F, F, F, F, T, 4, F, 0, F, F, F, 0, F, RemoveWhy.WorkTime);
            Rw(check, w, F, F, F, F, F, T, 5, F, 0, F, F, F, 0, T, RemoveWhy.WorkTime);
            Rw(check, w, F, F, F, F, F, T, 9, F, 0, F, F, F, 0, T, RemoveWhy.WorkTime);
            Rw(check, w, F, F, F, F, F, F, 0, T, 10, F, F, F, 0, T, RemoveWhy.WorkTime);
            Rw(check, w, F, F, F, F, T, F, 0, F, 0, T, T, T, 255, T, RemoveWhy.Bedtime);
            Rw(check, w, F, F, F, F, F, F, 0, F, 0, T, F, F, 0, T, RemoveWhy.Shopping);      // ResourceBuyer
            Rw(check, w, F, F, F, F, F, F, 0, F, 0, F, T, F, 255, T, RemoveWhy.Shopping);    // need taken beats need met
            Rw(check, w, F, F, F, F, F, T, 1, F, 0, F, F, F, 0, F, RemoveWhy.Shopping);      // purpose
            Rw(check, w, F, F, F, F, F, F, 0, T, 1, F, F, F, 0, T, RemoveWhy.Shopping);      // trip
            Rw(check, w, F, F, F, F, F, F, 0, F, 0, F, F, T, 255, T, RemoveWhy.VenueOff);
            Rw(check, w, F, F, F, F, F, F, 0, F, 0, F, F, F, 250, T, RemoveWhy.NeedMet);
            Rw(check, w, F, F, F, F, F, F, 0, F, 0, F, F, F, 253, F, RemoveWhy.NeedMet);     // beats no purpose
            Rw(check, w, F, F, F, F, F, F, 0, F, 0, F, F, F, 249, T, RemoveWhy.Unknown);
            Rw(check, w, F, F, F, F, F, F, 0, F, 0, F, F, F, 0, F, RemoveWhy.NoPurpose);
            Rw(check, w, F, F, F, F, F, T, 3, F, 0, F, F, F, 0, F, RemoveWhy.Unknown);       // going home, cause unseen
            Rw(check, w, F, F, F, F, F, T, 6, F, 0, F, F, F, 0, F, RemoveWhy.Unknown);       // Sleeping is not a lodge trace
            Rw(check, w, F, F, F, F, F, F, 0, T, 3, F, F, F, 0, T, RemoveWhy.Unknown);
            Rw(check, w, F, F, F, F, F, T, 2, F, 0, F, F, F, 0, F, RemoveWhy.Unknown);       // Leisure purpose, no Leisure

            // Sweep a: moving away beats every other trace — ill, work, bed, buyer, need, venue, at (2^7), purpose none /
            // Shopping / GoingToWork, counter 0 / 255 (768 cases); money false → MovingAway, money true → MoneyCheck.
            byte[] purposes = { 0, 1, 4 };
            for (int m = 0; m < 2; m++)
            {
                bool money = m == 1;
                RemoveWhy expected = money ? RemoveWhy.MoneyCheck : RemoveWhy.MovingAway;
                int bad = 0, cases = 0;
                string first = null;
                for (int mask = 0; mask < 128; mask++)
                {
                    for (int pi = 0; pi < purposes.Length; pi++)
                    {
                        for (int ci = 0; ci < 2; ci++)
                        {
                            int counter = ci == 0 ? 0 : 255;
                            RemoveWhy got = SkiDayTiming.RemoveWhyOf(true, money, (mask & 1) != 0, (mask & 2) != 0, (mask & 4) != 0,
                                                                     pi > 0, purposes[pi], false, 0, (mask & 8) != 0, (mask & 16) != 0,
                                                                     (mask & 32) != 0, counter, (mask & 64) != 0);
                            cases++;
                            if (got != expected)
                            {
                                bad++;
                                first = first ?? $"mask {mask}, purpose {purposes[pi]}, counter {counter} → {got}";
                            }
                        }
                    }
                }
                check(bad == 0 && cases == 768, w, $"sweep a (money {money}): moving away first → {expected}: {bad} of {cases} bad (first: {first})");
            }

            // Sweep b: no trace, in the lodge: counter 0-249 → Unknown, 250-255 → NeedMet (256 cases).
            int badB = 0;
            string firstB = null;
            for (int counter = 0; counter < 256; counter++)
            {
                RemoveWhy got = SkiDayTiming.RemoveWhyOf(false, false, false, false, false, false, 0, false, 0, false, false, false, counter, true);
                RemoveWhy expected = counter >= 250 ? RemoveWhy.NeedMet : RemoveWhy.Unknown;
                if (got != expected)
                {
                    badB++;
                    firstB = firstB ?? $"counter {counter} → {got}";
                }
            }
            check(badB == 0, w, $"sweep b: counter 0-249 → Unknown, 250-255 → NeedMet: {badB} of 256 bad (first: {firstB})");
        }

        private static void Rw(Func<bool, string, string, bool> check, string w, bool moving, bool money, bool ill, bool work, bool bed,
                               bool hasP, byte p, bool hasT, byte t, bool buyer, bool need, bool venue, int counter, bool at, RemoveWhy expected)
        {
            RemoveWhy got = SkiDayTiming.RemoveWhyOf(moving, money, ill, work, bed, hasP, p, hasT, t, buyer, need, venue, counter, at);
            check(got == expected, w, $"RemoveWhyOf(moving {moving}, money {money}, ill {ill}, work {work}, bed {bed}, purpose {hasP}/{p}, " +
                                      $"trip {hasT}/{t}, buyer {buyer}, need {need}, venue {venue}, counter {counter}, at {at}) = {got}, expected {expected}");
        }

        // ST33 NoPurposeAtOf(inBuilding, atOrigin, atOutsideConnection, penalty) (10)
        private static void NoPurposeRows(Func<bool, string, string, bool> check)
        {
            const string w = "skiday-timing ST33";
            Np(check, w, true, true, false, 255, NoPurposeAt.PlacedHome);
            Np(check, w, true, true, false, 250, NoPurposeAt.PlacedHome);
            Np(check, w, true, true, false, 249, NoPurposeAt.Home);
            Np(check, w, true, false, true, 0, NoPurposeAt.MapEdge);
            Np(check, w, true, false, true, 255, NoPurposeAt.MapEdge);
            Np(check, w, true, true, true, 255, NoPurposeAt.MapEdge);     // the edge beats home
            Np(check, w, true, false, false, 254, NoPurposeAt.PlacedElsewhere);
            Np(check, w, true, false, false, 0, NoPurposeAt.Elsewhere);
            Np(check, w, false, false, false, 255, NoPurposeAt.NoBuilding);
            Np(check, w, false, true, true, 0, NoPurposeAt.NoBuilding);
        }

        private static void Np(Func<bool, string, string, bool> check, string w, bool inB, bool atO, bool edge, int penalty, NoPurposeAt expected)
        {
            NoPurposeAt got = SkiDayTiming.NoPurposeAtOf(inB, atO, edge, penalty);
            check(got == expected, w, $"NoPurposeAtOf({inB}, {atO}, {edge}, {penalty}) = {got}, expected {expected}");
        }

        // ST34 follow constants (8)
        private static void FollowConstRows(Func<bool, string, string, bool> check)
        {
            const string w = "skiday-timing ST34";
            check(SkiDayTiming.FollowWatchFrames == 10922u && SkiDayTiming.FollowWatchFrames == SkiDayTiming.FramesFor(1.0), w,
                  $"FollowWatchFrames {SkiDayTiming.FollowWatchFrames} = 10,922 = FramesFor(1) {SkiDayTiming.FramesFor(1.0)}");
            check(SkiDayTiming.NearLodgeM == 100f, w, $"NearLodgeM {SkiDayTiming.NearLodgeM:R} = 100");
            check(SkiDayTiming.ResidentWaitingTransport == 4u && SkiDayTiming.ResidentHangaround == 16u && SkiDayTiming.ResidentInVehicle == 32u &&
                  SkiDayTiming.ResidentDummyTraffic == 8192u, w, "ResidentFlags literals 4 / 16 / 32 / 8192 (ResidentFlags.cs)");
            check(SkiDayTiming.FollowResultCount == 6 && SkiDayTiming.StillOutCount == 5 && SkiDayTiming.WalkerKindCount == 5 &&
                  SkiDayTiming.FollowExitKinds == 3, w, "counts 6 / 5 / 5 / 3");
            check((byte)FollowResult.Home == 0 && (byte)FollowResult.MapExit == 1 && (byte)FollowResult.OtherBuilding == 2 &&
                  (byte)FollowResult.BackInLodge == 3 && (byte)FollowResult.Deleted == 4 && (byte)FollowResult.StillOut == 5, w, "FollowResult literals 0..5");
            check((byte)StillOut.NoWalker == 0 && (byte)StillOut.Riding == 1 && (byte)StillOut.Waiting == 2 && (byte)StillOut.FailedPath == 3 &&
                  (byte)StillOut.OnFoot == 4, w, "StillOut literals 0..4");
            check((byte)WalkerKind.Walking == 0 && (byte)WalkerKind.Waiting == 1 && (byte)WalkerKind.FailedPath == 2 &&
                  (byte)WalkerKind.Hangaround == 3 && (byte)WalkerKind.Skip == 4, w, "WalkerKind literals 0..4");
            check(SkiDayTiming.FollowFlagFollowed == 1 && SkiDayTiming.FollowFlagWalkerSeen == 2 && SkiDayTiming.FollowFlagWalkerLost == 4 &&
                  SkiDayTiming.FollowFlagPlaced == 8 && SkiDayTiming.FollowFlagAtOnce == 16 && SkiDayTiming.FollowFlagNotFollowedCap == 32, w,
                  "FollowFlag bits 1, 2, 4, 8, 16, 32");
        }

        // ST35 FollowExitKind(exitWay) (13)
        private static void FollowKindRows(Func<bool, string, string, bool> check)
        {
            const string w = "skiday-timing ST35";
            Fk(check, w, 0, 0);     // Home
            Fk(check, w, 8, 1);     // NoPurpose
            Fk(check, w, 4, 2);     // MovingAway
            byte[] others = { 1, 2, 3, 5, 6, 7, 9, 10, 11, 255 };
            for (int i = 0; i < others.Length; i++)
                Fk(check, w, others[i], -1);
        }

        private static void Fk(Func<bool, string, string, bool> check, string w, byte exitWay, int expected)
        {
            int got = SkiDayTiming.FollowExitKind(exitWay);
            check(got == expected, w, $"FollowExitKind({exitWay}) = {got}, expected {expected}");
        }

        // ST36 PlacedNow(firstSample, penaltyNow) (5)
        private static void PlacedRows(Func<bool, string, string, bool> check)
        {
            const string w = "skiday-timing ST36";
            Pn(check, w, true, 0, true);
            Pn(check, w, false, 255, true);
            Pn(check, w, false, 250, true);
            Pn(check, w, false, 249, false);
            Pn(check, w, false, 0, false);
        }

        private static void Pn(Func<bool, string, string, bool> check, string w, bool first, byte penalty, bool expected)
        {
            bool got = SkiDayTiming.PlacedNow(first, penalty);
            check(got == expected, w, $"PlacedNow({first}, {penalty}) = {got}, expected {expected}");
        }

        // ST37 TryFollow (exit 1,000,000 unless named) (15)
        private static void TryFollowRows(Func<bool, string, string, bool> check)
        {
            const string w = "skiday-timing ST37";
            const uint x = 1000000u;
            // (gone, inB, home, edge, lodge, first, penalty, now, exit) → (done, result, placed)
            Tf(check, w, true, true, true, false, false, true, 0, 1000000u, x, true, FollowResult.Deleted, false);
            Tf(check, w, false, true, true, false, false, true, 0, 1000000u, x, true, FollowResult.Home, true);
            Tf(check, w, false, true, true, false, false, false, 254, 1000640u, x, true, FollowResult.Home, true);
            Tf(check, w, false, true, true, false, false, false, 9, 1000640u, x, true, FollowResult.Home, false);
            Tf(check, w, false, true, false, true, false, true, 0, 1000000u, x, true, FollowResult.MapExit, true);
            Tf(check, w, false, true, false, true, false, false, 120, 1005000u, x, true, FollowResult.MapExit, false);
            Tf(check, w, false, true, false, false, true, false, 255, 1000640u, x, true, FollowResult.BackInLodge, false);
            Tf(check, w, false, true, false, false, false, true, 0, 1000000u, x, true, FollowResult.OtherBuilding, true);
            Tf(check, w, false, true, false, false, false, false, 30, 1000640u, x, true, FollowResult.OtherBuilding, false);
            Tf(check, w, false, true, true, true, false, false, 5, 1000640u, x, true, FollowResult.Home, false);    // home beats the edge
            Tf(check, w, false, false, false, false, false, true, 0, 1000000u, x, false, FollowResult.StillOut, false);
            Tf(check, w, false, false, false, false, false, true, 0, 1010921u, x, false, FollowResult.StillOut, false);
            Tf(check, w, false, false, false, false, false, true, 0, 1010922u, x, true, FollowResult.StillOut, false);
            // the frame counter wraps: exit 4,294,967,000 + 10,922 = 10,626 (mod 2^32)
            Tf(check, w, false, false, false, false, false, false, 0, 10626u, 4294967000u, true, FollowResult.StillOut, false);
            Tf(check, w, false, false, false, false, false, false, 0, 10625u, 4294967000u, false, FollowResult.StillOut, false);
        }

        private static void Tf(Func<bool, string, string, bool> check, string w, bool gone, bool inB, bool home, bool edge, bool lodge, bool first,
                               byte penalty, uint now, uint exit, bool expectDone, FollowResult expectResult, bool expectPlaced)
        {
            bool done = SkiDayTiming.TryFollow(gone, inB, home, edge, lodge, first, penalty, now, exit, out FollowResult got, out bool placed);
            check(done == expectDone && (!done || (got == expectResult && placed == expectPlaced)), w,
                  $"TryFollow({gone}, {inB}, {home}, {edge}, {lodge}, {first}, {penalty}, {now}, {exit}) = ({done}, {got}, {placed}), " +
                  $"expected ({expectDone}, {expectResult}, {expectPlaced})");
        }

        // ST38 StillOutOf(walkerLive, riding, waiting, failed) (5)
        private static void StillOutRows(Func<bool, string, string, bool> check)
        {
            const string w = "skiday-timing ST38";
            So(check, w, false, true, true, true, StillOut.NoWalker);
            So(check, w, true, true, true, true, StillOut.Riding);
            So(check, w, true, false, true, true, StillOut.Waiting);
            So(check, w, true, false, false, true, StillOut.FailedPath);
            So(check, w, true, false, false, false, StillOut.OnFoot);
        }

        private static void So(Func<bool, string, string, bool> check, string w, bool live, bool riding, bool waiting, bool failed, StillOut expected)
        {
            StillOut got = SkiDayTiming.StillOutOf(live, riding, waiting, failed);
            check(got == expected, w, $"StillOutOf({live}, {riding}, {waiting}, {failed}) = {got}, expected {expected}");
        }

        // ST39 WaitHoursOfTimer(timer) (all exact in double) (6)
        private static void WaitHourRows(Func<bool, string, string, bool> check)
        {
            const string w = "skiday-timing ST39";
            Wh(check, w, 0, 0.0);
            Wh(check, w, -5, 0.0);
            Wh(check, w, 1024, 1.0);
            Wh(check, w, 250, 0.244140625);
            Wh(check, w, 5000, 4.8828125);        // the game's transit give-up, MAX_TRANSPORT_WAIT_TICKS
            Wh(check, w, 2147483647, 2097151.9990234375);
        }

        private static void Wh(Func<bool, string, string, bool> check, string w, int timer, double expected)
        {
            double got = SkiDayTiming.WaitHoursOfTimer(timer);
            check(got == expected, w, $"WaitHoursOfTimer({timer}) = {got:R}, expected {expected:R}");
        }

        // ST40 WalkerKindOf(aboard, residentFlags, divertPathFailed, pathBad) (12)
        private static void WalkerKindRows(Func<bool, string, string, bool> check)
        {
            const string w = "skiday-timing ST40";
            Wk(check, w, true, 4u, false, false, WalkerKind.Skip);         // aboard
            Wk(check, w, false, 32u, false, false, WalkerKind.Skip);       // InVehicle
            Wk(check, w, false, 8192u, false, false, WalkerKind.Skip);     // DummyTraffic
            Wk(check, w, false, 4u, false, false, WalkerKind.Waiting);
            Wk(check, w, false, 4u, true, true, WalkerKind.Waiting);       // waiting beats a failed path
            Wk(check, w, false, 0u, true, false, WalkerKind.FailedPath);
            Wk(check, w, false, 0u, false, true, WalkerKind.FailedPath);
            Wk(check, w, false, 16u, false, false, WalkerKind.Hangaround);
            Wk(check, w, false, 16u, true, false, WalkerKind.FailedPath);  // a failed path beats hanging around
            Wk(check, w, false, 0u, false, false, WalkerKind.Walking);
            Wk(check, w, false, 8u, false, false, WalkerKind.Walking);     // Arrived
            Wk(check, w, false, 1u, false, false, WalkerKind.Walking);     // Disembarking
        }

        private static void Wk(Func<bool, string, string, bool> check, string w, bool aboard, uint flags, bool divert, bool pathBad, WalkerKind expected)
        {
            WalkerKind got = SkiDayTiming.WalkerKindOf(aboard, flags, divert, pathBad);
            check(got == expected, w, $"WalkerKindOf({aboard}, {flags}, {divert}, {pathBad}) = {got}, expected {expected}");
        }

        // ST41 WithinXZ(p, center, radius) (7)
        private static void WithinRows(Func<bool, string, string, bool> check)
        {
            const string w = "skiday-timing ST41";
            var o = new Unity.Mathematics.float2(0f, 0f);
            Wx(check, w, new Unity.Mathematics.float2(100f, 0f), o, 100f, true);
            Wx(check, w, new Unity.Mathematics.float2(60f, 80f), o, 100f, true);           // exactly 10,000
            Wx(check, w, new Unity.Mathematics.float2(0f, 100.01f), o, 100f, false);
            Wx(check, w, new Unity.Mathematics.float2(70.72f, 70.72f), o, 100f, false);    // 10,002.6
            Wx(check, w, new Unity.Mathematics.float2(float.NaN, 0f), o, 100f, false);
            Wx(check, w, o, new Unity.Mathematics.float2(float.NaN, 0f), 100f, false);
            Wx(check, w, o, o, 0f, true);                                                    // radius 0 at the center
        }

        private static void Wx(Func<bool, string, string, bool> check, string w, Unity.Mathematics.float2 p, Unity.Mathematics.float2 c, float r,
                               bool expected)
        {
            bool got = SkiDayTiming.WithinXZ(p, c, r);
            check(got == expected, w, $"WithinXZ(({p.x:R}, {p.y:R}), ({c.x:R}, {c.y:R}), {r:R}) = {got}, expected {expected}");
        }

        // ST42 FollowSlot / AtOnceSlot (incl. the one-to-one property as one check) (12)
        private static void SlotRows(Func<bool, string, string, bool> check)
        {
            const string w = "skiday-timing ST42";
            Fsl(check, w, 0, 0, false, 0);
            Fsl(check, w, 0, 0, true, 1);
            Fsl(check, w, 1, 2, true, 17);
            Fsl(check, w, 2, 5, true, 35);
            Fsl(check, w, 3, 0, false, -1);
            Fsl(check, w, -1, 0, false, -1);
            Fsl(check, w, 0, 6, false, -1);
            var seen = new bool[36];
            int bad = 0;
            for (int k = 0; k < 3; k++)
            {
                for (int r = 0; r < 6; r++)
                {
                    for (int p = 0; p < 2; p++)
                    {
                        int s = SkiDayTiming.FollowSlot(k, r, p == 1);
                        if (s < 0 || s >= 36 || seen[s])
                            bad++;
                        else
                            seen[s] = true;
                    }
                }
            }
            check(bad == 0, w, $"FollowSlot maps the 36 valid (kind, result, placed) one-to-one onto 0..35: {bad} bad");
            Aos(check, w, 0, 0, 0);
            Aos(check, w, 2, 2, 8);
            Aos(check, w, 0, 3, -1);
            Aos(check, w, 3, 0, -1);
        }

        private static void Fsl(Func<bool, string, string, bool> check, string w, int kind, int result, bool placed, int expected)
        {
            int got = SkiDayTiming.FollowSlot(kind, result, placed);
            check(got == expected, w, $"FollowSlot({kind}, {result}, {placed}) = {got}, expected {expected}");
        }

        private static void Aos(Func<bool, string, string, bool> check, string w, int kind, int result, int expected)
        {
            int got = SkiDayTiming.AtOnceSlot(kind, result);
            check(got == expected, w, $"AtOnceSlot({kind}, {result}) = {got}, expected {expected}");
        }

        // ST43 FailureEvidence(o, departed, place, turnedBack) (12)
        private static void FailureEvidenceRows(Func<bool, string, string, bool> check)
        {
            const string w = "skiday-timing ST43";
            Fe(check, w, 0, true, Place.Road, false, SpeedEvidence.Bound);
            Fe(check, w, 1, true, Place.Road, false, SpeedEvidence.Bound);
            Fe(check, w, 2, true, Place.Road, false, SpeedEvidence.None);           // an expiry: booked at its late watch end
            Fe(check, w, 0, false, Place.Road, false, SpeedEvidence.None);          // never left
            Fe(check, w, 0, true, Place.Road, true, SpeedEvidence.None);            // turned back
            Fe(check, w, 0, true, Place.HomeCameBack, false, SpeedEvidence.None);
            Fe(check, w, 0, true, Place.HomeNeverLeft, false, SpeedEvidence.None);
            Fe(check, w, 0, true, Place.OtherBuilding, false, SpeedEvidence.None);
            Fe(check, w, 0, true, Place.Lodge, false, SpeedEvidence.None);
            Fe(check, w, 0, true, Place.Deleted, false, SpeedEvidence.None);
            Fe(check, w, 3, true, Place.Road, false, SpeedEvidence.None);
            Fe(check, w, -1, true, Place.Road, false, SpeedEvidence.None);
        }

        private static void Fe(Func<bool, string, string, bool> check, string w, int o, bool dep, Place place, bool turned, SpeedEvidence expected)
        {
            SpeedEvidence got = SkiDayTiming.FailureEvidence(o, dep, place, turned);
            check(got == expected, w, $"FailureEvidence({o}, {dep}, {place}, {turned}) = {got}, expected {expected}");
        }

        // ST44 LateEvidence(r, departed, place, turnedBack): FailureEvidence's predicate with place at the late end; the two rows the late
        // end actually produces for a deleted / gave-up guest
        private static void LateEvidenceRows(Func<bool, string, string, bool> check)
        {
            const string w = "skiday-timing ST44";
            Le(check, w, LateResult.ArrivedLate, true, Place.Road, false, SpeedEvidence.Exact);
            Le(check, w, LateResult.ArrivedLate, false, Place.HomeNeverLeft, false, SpeedEvidence.Exact);   // an arrival is exact anyway
            Le(check, w, LateResult.GaveUp, true, Place.Road, false, SpeedEvidence.Bound);
            Le(check, w, LateResult.Deleted, true, Place.Road, false, SpeedEvidence.Bound);
            Le(check, w, LateResult.Deleted, true, Place.Deleted, false, SpeedEvidence.None); // what PlaceOf reads at the late end
            Le(check, w, LateResult.GaveUp, true, Place.OtherBuilding, false, SpeedEvidence.None);   // GaveUp is in a building
            Le(check, w, LateResult.NotWithin6h, true, Place.Road, false, SpeedEvidence.Bound);
            Le(check, w, LateResult.GaveUp, false, Place.HomeNeverLeft, false, SpeedEvidence.None);
            Le(check, w, LateResult.NotWithin6h, true, Place.HomeCameBack, false, SpeedEvidence.None);
            Le(check, w, LateResult.Deleted, true, Place.OtherBuilding, false, SpeedEvidence.None);
            Le(check, w, LateResult.NotWithin6h, true, Place.Road, true, SpeedEvidence.None);
            Le(check, w, (LateResult)4, true, Place.Road, false, SpeedEvidence.None);
        }

        private static void Le(Func<bool, string, string, bool> check, string w, LateResult r, bool dep, Place place, bool turned, SpeedEvidence expected)
        {
            SpeedEvidence got = SkiDayTiming.LateEvidence(r, dep, place, turned);
            check(got == expected, w, $"LateEvidence({r}, {dep}, {place}, {turned}) = {got}, expected {expected}");
        }

        // ST45 pull constants (2)
        private static void PullConstRows(Func<bool, string, string, bool> check)
        {
            const string w = "skiday-timing ST45";
            check(SkiDayTiming.OnSiteGraceFrames == 21845u && SkiDayTiming.PullMinFrames == 64u && SkiDayTiming.PullNone == 0 &&
                  SkiDayTiming.PullNoRun == 1 && SkiDayTiming.PullNightClosed == 2, w,
                  $"OnSiteGraceFrames {SkiDayTiming.OnSiteGraceFrames} = 21,845, PullMinFrames {SkiDayTiming.PullMinFrames} = 64, reasons " +
                  $"{SkiDayTiming.PullNone} / {SkiDayTiming.PullNoRun} / {SkiDayTiming.PullNightClosed} = 0 / 1 / 2");
            int others = SkiDayTiming.Flag2TurnedBack | SkiDayTiming.Flag2WalkerLost | SkiDayTiming.Flag2WalkerPathFailed |
                         SkiDayTiming.Flag2VehiclePathFailed | SkiDayTiming.Flag2NeedOpen | SkiDayTiming.Flag2NeedOpenBefore;
            check(SkiDayTiming.Flag2DeadlinePulled == 64 && (SkiDayTiming.Flag2DeadlinePulled & others) == 0 && others == 63, w,
                  $"Flag2DeadlinePulled {SkiDayTiming.Flag2DeadlinePulled} = 64, distinct from the Flags2 bits 1, 2, 4, 8, 16, 32 (their union {others})");
        }

        // ST46 PullDeadline (now 1,000,000; deadlineIsClosing true unless named) (12)
        private static void PullRows(Func<bool, string, string, bool> check)
        {
            const string w = "skiday-timing ST46";
            const byte None = SkiDayTiming.PullNone, NoRun = SkiDayTiming.PullNoRun, Night = SkiDayTiming.PullNightClosed;
            // (known, held, runs, nightRunOpen, deadlineIsClosing, time of day, deadline) → (pulled?, frame, reason)
            Pd(check, w, true, false, 3, false, true, 0.5f, 1098304u, true, 1049152u, Night);     // 12:00: close 16:30 = +49,152; deadline 21:00
            Pd(check, w, true, false, 2, false, true, 0.625f, 1065536u, true, 1021845u, Night);   // 15:00: close 16:30 = +16,384, the 2 h floor wins
            Pd(check, w, true, false, 0, false, true, 0.625f, 1065536u, true, 1021845u, NoRun);   // no counted run: close now, the floor
            Pd(check, w, true, false, 0, false, true, 0.625f, 1021900u, false, 1021900u, None);   // 1,021,845 + 64 > 1,021,900
            Pd(check, w, true, false, 3, false, true, 0.75f, 1032768u, true, 1021845u, Night);    // 18:00, past 16:30: close now
            Pd(check, w, true, false, 3, true, true, 0.75f, 1032768u, false, 1032768u, None);     // the night run is open: close 21:00 = deadline
            Pd(check, w, true, false, 3, false, true, 0.1f, 1100000u, false, 1100000u, None);     // 02:24, before 05:00
            Pd(check, w, true, true, 0, false, true, 0.625f, 1065536u, false, 1065536u, None);    // held
            Pd(check, w, false, false, 0, false, true, 0.625f, 1065536u, false, 1065536u, None);  // no score
            Pd(check, w, true, false, 3, false, false, 0.5f, 1098304u, false, 1098304u, None);    // row 1, deadline not the closing (flap guard)
            Pd(check, w, true, false, 3, false, true, 0.5f, 1098000u, false, 1098000u, None);     // row 1, deadline not today's 21:00 frame
            Pd(check, w, true, false, 0, false, false, 0.625f, 1065536u, true, 1021845u, NoRun);  // row 3, own deadline: no run pulls it too
        }

        private static void Pd(Func<bool, string, string, bool> check, string w, bool known, bool held, int runs, bool night, bool closing, float t,
                               uint deadline, bool expectDone, uint expectFrame, byte expectReason)
        {
            bool done = SkiDayTiming.PullDeadline(known, held, runs, night, closing, 1000000u, t, deadline, out uint pulled, out byte reason);
            check(done == expectDone && pulled == expectFrame && reason == expectReason, w,
                  $"PullDeadline({known}, {held}, {runs}, {night}, {closing}, 1000000, {t:R}, {deadline}) = ({done}, {pulled}, {reason}), " +
                  $"expected ({expectDone}, {expectFrame}, {expectReason})");
        }
    }
}
