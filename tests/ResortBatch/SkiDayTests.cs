// SkiDayTests — Round S1 test file for tests/ResortBatch (ski-days spec ch1-rules.md §7, rows A-Q: the pure
// SkiDayRules, the F2 PowerOf hysteresis, the SkiResortScore additions). Owner: stream A (docs/research/ski-days/
// s1-spec.md §4.3). Created empty by stage 0 (2026-10-05, s1-spec §4.2 step 9): the e1-spec R9 convention —
// `public static class SkiDayTests` with `public static void Run(Func<bool, string, string, bool> check)` and its
// own Near; Program.Main calls SkiDayTests.Run(Check). Oracles are literal hand values (ch1 §7-§8).
//
// Written by stream A (2026-10-05). Sections, in run order (ch1 §7 letters; doubles ±1e-9 unless "exact",
// geometry in float ±0.01; times now = 1,000,000):
//   A  constants guard: every ch1 §2.2 row against its literal, the §2.3 enum literals, SkiDayLimit.
//   B  CheckCitizen: the base resident / tourist and the one-flip table (who and the reject, both kinds).
//   C  CheckLodge: each flag, the 800 m edge, NaN, the order.
//   D  AreaStateOf: the base and one change each, the hour edges, the capacity edge; HourOf / InClaimWindow.
//   E  ClassOf, WithinReach, DistanceFactor, TravelHours, the speed tables.
//   F  TryClosingFrame (the 12 × 2 table, exact frames), ClaimFits, ClaimDeadline.
//   G  Chance oracles R1-R15, T1-T5, X1-X4; PriceFactor, OpenShare, SnowFactor, the knobs, IsBetter.
//   H  Chance properties, seed 20261005, 20,000 random sets: one check per property (violations counted).
//   I  Rolls: the four exact values, Mix64 parity with RunSnowRules, and the statistics over 100,000 draws
//      (hit rate within 4σ, χ² over 20 bins, joint rate of consecutive frames / days).
//   J  Back-off (Confidence, RecordOutcome, UpdateCannotReach with its hysteresis) and LearnSpeed.
//   K  Powder: IsSnowHour, PowderOf, PowderMark, SnowHourSampleDate (= RunSnowRules.DayMeanC's dates, exact).
//   L  Capacity, CapacityPeople, HasRoom, and the capacity properties (20,000 random).
//   M  Slices (the five-frame table × 16 slices) and ArmCheck, IsPowerOfTwo, the SliceOf guard.
//   N  DayKey (rows; = SkiMoneyRules.MonthKey at 12 days over 100,000 random dates); Advance(ref MonthFlags) =
//      SkiResortLedger.Advance over 2,000 random key sequences × 50 steps.
//   O  Slope-side geometry: box, segment, ray crossing, the polygon walk (square, the R-3 strip).
//   P  F2: LiftServedRule.PowerOf(flags, wanted, fulfilled, last), both states; the 1-argument overload.
//   Q  Score additions on the ScoreTests compositions (RunsValueFull, RunsCapacity, NightRunsCounted, TopKnown /
//      TopM, OpenShare), SkiersPerHa, RunSkiersPerDay, and the 5,000-area properties (seed 20261005).
//   S  The ch1 §8 worked scenarios (FT8 hill, tiny hill, large resort): capacity, chances, travel times, the
//      last claim time of a day.
//   T  Round M1 "measure first" (docs/research/ski-days/measure/m1-spec.md §4, builder A 2026-10-05): DistanceBand;
//      LegOf / KindBitOf / MainMode (arrival-modes.md §9 rows) and the 0..255 legs partition; the TransportType
//      literals; FitVerdict rows and its property against ClaimFits (5,000 rows, seed 20261005); HourDate (=
//      RunSnowRules.HourAlignedDate) and MonthOfHour; Advance(ref f, key, closedShort) rows and (fixer R7) its parity
//      with SkiResortLedger.Advance(key, closedMonthShort) over 2,000 random sequences with a random short flag.
// Property sections count their violations and report once per property (the first offender in the message), so
// the check count stays small and fixed.

using System;
using System.Collections.Generic;
using SkiResortMod.Systems;
using Unity.Mathematics;
using Rules = SkiResortMod.Systems.SkiDayRules;
using Score = SkiResortMod.Systems.SkiResortScore;
using GK = SkiResortMod.Systems.SkiDayRules.GuestKind;
using TC = SkiResortMod.Systems.SkiDayRules.TravelClass;
using Reject = SkiResortMod.Systems.SkiDayRules.Reject;
using LodgeReject = SkiResortMod.Systems.SkiDayRules.LodgeReject;
using AreaState = SkiResortMod.Systems.SkiDayRules.AreaState;
using MonthStep = SkiResortMod.Systems.SkiDayRules.MonthStep;
using ArmVerdict = SkiResortMod.Systems.SkiDayRules.ArmVerdict;

namespace ResortBatch
{
    public static class SkiDayTests
    {
        private static Func<bool, string, string, bool> s_Check;

        private const uint Now = 1000000u;

        public static void Run(Func<bool, string, string, bool> check)
        {
            s_Check = check;
            Constants();          // A
            Citizen();            // B
            Lodge();              // C
            Area();               // D
            Travel();             // E
            Deadline();           // F
            Chances();            // G
            ChanceProperties();   // H
            Rolls();              // I
            BackOff();            // J
            Powder();             // K
            CapacityRows();       // L
            Slices();             // M
            DayAndMonth();        // N
            Geometry();           // O
            Power();              // P
            ScoreAdditions();     // Q
            Scenarios();          // S (ch1 §8)
            MeasureRules();       // T (Round M1)
        }

        private static bool Check(bool cond, string where, string msg)
        {
            return s_Check(cond, where, msg);
        }

        private static bool Near(double a, double b, double eps)
        {
            return Math.Abs(a - b) <= eps;
        }

        private static string R(double x)
        {
            return x.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        }

        // ================================================================== A. constants guard (ch1 §2.2-§2.3)

        private static void Constants()
        {
            const string w = "skiday A const";
            Check(Rules.TicksPerDay == 262144 && Rules.FramesPerHour == 262144 / 24.0 && Near(Rules.FramesPerHour, 10922.667, 0.001), w, "TicksPerDay / FramesPerHour");
            Check(Rules.SliceGroups == 16 && Rules.MaxFramesPerRenderFrame == 8 && Rules.MinLeisureToTripRatio == 4, w, "SliceGroups 16, MaxFramesPerRenderFrame 8, MinLeisureToTripRatio 4");
            Check(Rules.NoMonth == int.MinValue && Rules.NoMonth == SkiResortLedger.NoMonth, w, "NoMonth = int.MinValue = SkiResortLedger.NoMonth");
            Check(Rules.ClaimStartHour == 5.0 && Rules.LiftsOpenHour == 8.0 && Rules.CloseHour == 16.5 && Rules.NightCloseHour == 21.0, w, "hours 5 / 8 / 16.5 / 21");
            Check(Rules.MinStayHours == 2.0 && Rules.WalkMaxM == 900.0 && Rules.NearKm == 1.0 && Rules.DetourFactor == 1.3, w, "MinStayHours 2, WalkMaxM 900, NearKm 1, DetourFactor 1.3");
            Check(Rules.StartSpeedWalk == 0.3 && Rules.StartSpeedCar == 2.2 && Rules.StartSpeedNoCar == 1.2, w, "start speeds 0.3 / 2.2 / 1.2");
            Check(Rules.MinSpeedWalk == 0.1 && Rules.MaxSpeedWalk == 1.0 && Rules.MinSpeedCar == 0.5 && Rules.MaxSpeedCar == 8.0 &&
                  Rules.MinSpeedNoCar == 0.2 && Rules.MaxSpeedNoCar == 8.0, w, "speed clamps 0.1/1, 0.5/8, 0.2/8");
            Check(Rules.SpeedLearnRate == 0.25 && Rules.MinLearnDistanceM == 300.0 && Rules.MinLearnHours == 0.05 && Rules.MinTravelSpeed == 0.01, w,
                  "SpeedLearnRate 0.25, MinLearnDistanceM 300, MinLearnHours 0.05, MinTravelSpeed 0.01");
            Check(Rules.ConfidenceMin == 0.1 && Rules.PriorWeight == 4.0 && Rules.PriorWalk == 1.0 && Rules.PriorCar == 1.0 && Rules.PriorNoCar == 0.1, w,
                  "ConfidenceMin 0.1, PriorWeight 4, priors 1 / 1 / 0.1");
            Check(Rules.TrackKeep == 0.975 && Near(Rules.TrackKeep, 1.0 - 1.0 / 40.0, 1e-15), w, "TrackKeep 0.975 (= 1 − 1/40)");
            Check(Rules.CannotReachMinResolved == 20 && Rules.CannotReachBelow == 0.5 && Rules.CannotReachClearAt == 0.6, w, "cannot-reach 20 / 0.5 / 0.6");
            Check(Rules.ResidentCap == 0.20 && Rules.TouristCap == 0.30 && Rules.TouristPowderShare == 0.25, w, "caps 0.2 / 0.3, tourist powder share 0.25");
            Check(Rules.PriceFullAt == 240 && Rules.PriceSpan == 200 && Rules.PriceMax == 200, w, "price 240 / 200 / 200");
            Check(Rules.MaxAttraction == 200 && Rules.MaxAttraction == Score.MaxAttraction, w, "MaxAttraction 200 = SkiResortScore.MaxAttraction");
            Check(Rules.SnowPrecipMin == 0.1f && Rules.SnowHourSamples == 24 && Rules.SnowHourSamples == RunSnowRules.DayMeanSamples, w,
                  "SnowPrecipMin 0.1f, SnowHourSamples 24 = RunSnowRules.DayMeanSamples");
            Check(Rules.PowderStartHours == 4 && Rules.PowderFullHours == 10 && Rules.PowderMarkAt == 0.5, w, "powder 4 / 10 / mark 0.5");
            Check(Rules.LodgeSkiersPerDay == 360.0 && Rules.PowderCapacityBoost == 0.25 && Rules.MaxCapacityPeople == 1000000, w, "lodge 360, boost 0.25, max 1e6");
            Check(Rules.SlopesideMaxM == 800f, w, "SlopesideMaxM 800f");
            Check(Rules.LocalsPctDefault == 25 && Rules.LocalsPctMin == 0 && Rules.LocalsPctMax == 50, w, "Locals 25 / 0 / 50");
            Check(Rules.TouristsPctDefault == 50 && Rules.TouristsPctMin == 0 && Rules.TouristsPctMax == 100, w, "Tourists 50 / 0 / 100");
            Check(Rules.ReachKmDefault == 7 && Rules.ReachKmMin == 2 && Rules.ReachKmMax == 12, w, "Reach 7 / 2 / 12");
            Check(Rules.PowderPctDefault == 100 && Rules.PowderPctMin == 0 && Rules.PowderPctMax == 200, w, "Powder 100 / 0 / 200");
            Check(Rules.ResidentSalt == 0x534B494441595331UL && Rules.TouristSalt == 0x534B494441595332UL &&
                  Ascii(Rules.ResidentSalt) == "SKIDAYS1" && Ascii(Rules.TouristSalt) == "SKIDAYS2", w, "salts \"SKIDAYS1\" / \"SKIDAYS2\"");

            // Enum literals (§2.1, §2.3): the DUMP counters index by them; SkiDayLimit is gate R17's SKI_DAY_LIMIT.
            Check((byte)SkiDayLimit.None == 0 && (byte)SkiDayLimit.Runs == 1 && (byte)SkiDayLimit.Lodges == 2, w, "SkiDayLimit 0/1/2");
            Check((byte)GK.Resident == 0 && (byte)GK.Tourist == 1, w, "GuestKind 0/1");
            Check((byte)TC.Walk == 0 && (byte)TC.Car == 1 && (byte)TC.NoCarFar == 2 && (byte)TC.None == 3, w, "TravelClass 0-3");
            Check((byte)Reject.None == 0 && (byte)Reject.NotDeciding == 1 && (byte)Reject.Busy == 2 && (byte)Reject.Ill == 3 &&
                  (byte)Reject.TripsPending == 4 && (byte)Reject.NotInBuilding == 5 && (byte)Reject.HouseholdExcluded == 6 &&
                  (byte)Reject.NotAtHome == 7 && (byte)Reject.OutingNotSpent == 8, w, "Reject 0-8");
            Check((byte)LodgeReject.None == 0 && (byte)LodgeReject.NotLodge == 1 && (byte)LodgeReject.NotInstalled == 2 &&
                  (byte)LodgeReject.Inactive == 3 && (byte)LodgeReject.Destroyed == 4 && (byte)LodgeReject.NoLeisureTag == 5 &&
                  (byte)LodgeReject.NoRoad == 6 && (byte)LodgeReject.NotSlopeside == 7, w, "LodgeReject 0-7");
            Check((byte)AreaState.Open == 0 && (byte)AreaState.NoScore == 1 && (byte)AreaState.Held == 2 && (byte)AreaState.NoEfficiency == 3 &&
                  (byte)AreaState.NoOpenRun == 4 && (byte)AreaState.NoLodge == 5 && (byte)AreaState.OutOfHours == 6 && (byte)AreaState.Full == 7,
                  w, "AreaState 0-7");
            Check((byte)MonthStep.None == (byte)SkiResortLedger.AdvanceResult.None && (byte)MonthStep.Started == (byte)SkiResortLedger.AdvanceResult.Started &&
                  (byte)MonthStep.Rolled == (byte)SkiResortLedger.AdvanceResult.Rolled && (byte)MonthStep.Jumped == (byte)SkiResortLedger.AdvanceResult.Jumped &&
                  (byte)MonthStep.Jumped == 3, w, "MonthStep 0-3 = SkiResortLedger.AdvanceResult");
            Check((byte)ArmVerdict.Ok == 0 && (byte)ArmVerdict.BadInterval == 1 && (byte)ArmVerdict.BadOffset == 2 &&
                  (byte)ArmVerdict.LeisureTooFast == 3 && (byte)ArmVerdict.BehaviorTooFast == 4, w, "ArmVerdict 0-4");

            // Unit01 (the helper every rule uses).
            Check(Rules.Unit01(double.NaN) == 0.0 && Rules.Unit01(-1.0) == 0.0 && Rules.Unit01(0.3) == 0.3 && Rules.Unit01(1.0) == 1.0 &&
                  Rules.Unit01(2.0) == 1.0 && Rules.Unit01(double.PositiveInfinity) == 1.0 && Rules.Unit01(double.NegativeInfinity) == 0.0,
                  w, "Unit01: NaN 0, −1 0, 0.3, 1, 2 → 1, ±∞");
        }

        private static string Ascii(ulong salt)
        {
            var c = new char[8];
            for (int i = 0; i < 8; i++)
                c[i] = (char)((salt >> (56 - 8 * i)) & 0xFF);
            return new string(c);
        }

        // ================================================================== B. CheckCitizen (ch1 §3.1)

        private delegate void Flip(ref Rules.CitizenFacts c);

        private static Rules.CitizenFacts BaseResident()
        {
            return new Rules.CitizenFacts { HasLeisure = true, LeisureTargetNull = true, HasCurrentBuilding = true, AtHome = true };
        }

        private static Rules.CitizenFacts BaseTourist()
        {
            Rules.CitizenFacts c = BaseResident();
            c.HouseholdTourist = true;
            c.HouseholdAttendingEvent = true;
            return c;
        }

        private static void Citizen()
        {
            const string w = "skiday B citizen";
            Rules.CitizenFacts res = BaseResident(), tour = BaseTourist();
            Check(Rules.CheckCitizen(res, out GK whoR) == Reject.None && whoR == GK.Resident, w, "base resident → None / Resident");
            Check(Rules.CheckCitizen(tour, out GK whoT) == Reject.None && whoT == GK.Tourist, w, "base tourist → None / Tourist");

            var rows = new (string name, Flip flip, Reject resident, Reject tourist)[]
            {
                ("HasLeisure false", (ref Rules.CitizenFacts c) => c.HasLeisure = false, Reject.NotDeciding, Reject.NotDeciding),
                ("LeisureTargetNull false", (ref Rules.CitizenFacts c) => c.LeisureTargetNull = false, Reject.NotDeciding, Reject.NotDeciding),
                ("PathInformation", (ref Rules.CitizenFacts c) => c.HasPathInformation = true, Reject.Busy, Reject.Busy),
                ("Target", (ref Rules.CitizenFacts c) => c.HasTarget = true, Reject.Busy, Reject.Busy),
                ("TravelPurpose", (ref Rules.CitizenFacts c) => c.HasTravelPurpose = true, Reject.Busy, Reject.Busy),
                ("ResourceBuyer", (ref Rules.CitizenFacts c) => c.HasResourceBuyer = true, Reject.Busy, Reject.Busy),
                ("AttendingMeeting", (ref Rules.CitizenFacts c) => c.HasAttendingMeeting = true, Reject.Busy, Reject.Busy),
                ("HealthProblem", (ref Rules.CitizenFacts c) => c.HasHealthProblem = true, Reject.Ill, Reject.Ill),
                ("TripCount 1", (ref Rules.CitizenFacts c) => c.TripCount = 1, Reject.TripsPending, Reject.TripsPending),
                ("HasCurrentBuilding false", (ref Rules.CitizenFacts c) => c.HasCurrentBuilding = false, Reject.NotInBuilding, Reject.NotInBuilding),
                ("Commuter", (ref Rules.CitizenFacts c) => c.HouseholdCommuter = true, Reject.HouseholdExcluded, Reject.None),
                ("Homeless", (ref Rules.CitizenFacts c) => c.HouseholdHomeless = true, Reject.HouseholdExcluded, Reject.None),
                ("MovingAway", (ref Rules.CitizenFacts c) => c.HouseholdMovingAway = true, Reject.HouseholdExcluded, Reject.HouseholdExcluded),
                ("LodgingSeeker", (ref Rules.CitizenFacts c) => c.HouseholdLodgingSeeker = true, Reject.None, Reject.HouseholdExcluded),
                ("AtHome false", (ref Rules.CitizenFacts c) => c.AtHome = false, Reject.NotAtHome, Reject.NotAtHome),
                ("AttendingEvent false", (ref Rules.CitizenFacts c) => c.HouseholdAttendingEvent = false, Reject.None, Reject.OutingNotSpent),
                ("TripCount 1 + AtHome false (order)", (ref Rules.CitizenFacts c) => { c.TripCount = 1; c.AtHome = false; }, Reject.TripsPending, Reject.TripsPending),
            };
            for (int i = 0; i < rows.Length; i++)
            {
                Rules.CitizenFacts r = BaseResident();
                rows[i].flip(ref r);
                Reject gotR = Rules.CheckCitizen(r, out GK kindR);
                Check(gotR == rows[i].resident && kindR == GK.Resident, w + " resident", $"{rows[i].name}: {gotR} / {kindR}, expected {rows[i].resident} / Resident");
                Rules.CitizenFacts t = BaseTourist();
                rows[i].flip(ref t);
                Reject gotT = Rules.CheckCitizen(t, out GK kindT);
                Check(gotT == rows[i].tourist && kindT == GK.Tourist, w + " tourist", $"{rows[i].name}: {gotT} / {kindT}, expected {rows[i].tourist} / Tourist");
            }
            // A resident ignores AttendingEvent either way (the base has it false; true changes nothing).
            Rules.CitizenFacts ev = BaseResident();
            ev.HouseholdAttendingEvent = true;
            Check(Rules.CheckCitizen(ev, out GK _) == Reject.None, w + " resident", "AttendingEvent true is ignored for residents");
        }

        // ================================================================== C. CheckLodge (ch1 §3.2)

        private static Rules.LodgeFacts GoodLodge(float nearest)
        {
            return new Rules.LodgeFacts
            {
                IsLodgePrefab = true, OnLiveSign = true, Active = true, Destroyed = false, LeisureTagged = true, HasRoad = true,
                NearestRunOrStationM = nearest,
            };
        }

        private static void Lodge()
        {
            const string w = "skiday C lodge";
            Check(Rules.CheckLodge(GoodLodge(500f)) == LodgeReject.None, w, "all good, nearest 500 → None");
            Rules.LodgeFacts l;
            l = GoodLodge(500f); l.IsLodgePrefab = false; Check(Rules.CheckLodge(l) == LodgeReject.NotLodge, w, "not a lodge (the sign) → NotLodge");
            l = GoodLodge(500f); l.OnLiveSign = false; Check(Rules.CheckLodge(l) == LodgeReject.NotInstalled, w, "no live sign → NotInstalled");
            l = GoodLodge(500f); l.Active = false; Check(Rules.CheckLodge(l) == LodgeReject.Inactive, w, "inactive → Inactive");
            l = GoodLodge(500f); l.Destroyed = true; Check(Rules.CheckLodge(l) == LodgeReject.Destroyed, w, "destroyed → Destroyed");
            l = GoodLodge(500f); l.LeisureTagged = false; Check(Rules.CheckLodge(l) == LodgeReject.NoLeisureTag, w, "no leisure tag → NoLeisureTag");
            l = GoodLodge(500f); l.HasRoad = false; Check(Rules.CheckLodge(l) == LodgeReject.NoRoad, w, "no road → NoRoad");
            Check(Rules.CheckLodge(GoodLodge(800f)) == LodgeReject.None, w, "nearest 800 (inclusive) → None");
            Check(Rules.CheckLodge(GoodLodge(800.01f)) == LodgeReject.NotSlopeside, w, "nearest 800.01 → NotSlopeside");
            Check(Rules.CheckLodge(GoodLodge(float.NaN)) == LodgeReject.NotSlopeside, w, "nearest NaN → NotSlopeside");
            Check(Rules.CheckLodge(GoodLodge(float.PositiveInfinity)) == LodgeReject.NotSlopeside, w, "nearest +∞ (no member) → NotSlopeside");
            l = GoodLodge(5000f); l.IsLodgePrefab = false; l.Destroyed = true;
            Check(Rules.CheckLodge(l) == LodgeReject.NotLodge, w, "not a lodge + destroyed + far → NotLodge (order)");
            l = GoodLodge(5000f); l.HasRoad = false; l.LeisureTagged = false;
            Check(Rules.CheckLodge(l) == LodgeReject.NoLeisureTag, w, "no tag + no road + far → NoLeisureTag (order)");
            // Fixer pass (review TL-1): Inactive before Destroyed (ch1 §3.2). Both fills (ClaimJob step A, the parking
            // census) set Active = hasBuilding && … and Destroyed = … || !hasBuilding, so a lodge that lost its Building
            // carries both and reads Inactive (review TL-X2: the verdict is right, only the label).
            l = GoodLodge(500f); l.Active = false; l.Destroyed = true;
            Check(Rules.CheckLodge(l) == LodgeReject.Inactive, w, "inactive + destroyed → Inactive (order)");
            // ch3 R10: the parking census asks with nearest 0 (never NotSlopeside).
            l = GoodLodge(0f);
            Check(Rules.CheckLodge(l) == LodgeReject.None, w, "the census form (nearest 0) → None");
            Check(Rules.IsSlopeside(800f) && !Rules.IsSlopeside(800.01f) && !Rules.IsSlopeside(float.NaN) && Rules.IsSlopeside(0f), w, "IsSlopeside edges");
        }

        // ================================================================== D. AreaStateOf (ch1 §3.3)

        private static Rules.AreaFacts BaseArea()
        {
            return new Rules.AreaFacts { ScoreKnown = true, WriteHeld = false, Efficiency = 1.04, RunsCounted = 5, SkiDayLodges = 1 };
        }

        private static void Area()
        {
            const string w = "skiday D area";
            const float nine = 0.375f;
            Rules.AreaFacts a = BaseArea();
            Check(Rules.AreaStateOf(a, nine, false, 0, 154) == AreaState.Open, w, "base (09:00, day, 0 of 154) → Open");
            a = BaseArea(); a.ScoreKnown = false; Check(Rules.AreaStateOf(a, nine, false, 0, 154) == AreaState.NoScore, w, "!known → NoScore");
            a = BaseArea(); a.WriteHeld = true; Check(Rules.AreaStateOf(a, nine, false, 0, 154) == AreaState.Held, w, "held → Held");
            a = BaseArea(); a.Efficiency = 0; Check(Rules.AreaStateOf(a, nine, false, 0, 154) == AreaState.NoEfficiency, w, "eff 0 → NoEfficiency");
            a = BaseArea(); a.Efficiency = double.NaN; Check(Rules.AreaStateOf(a, nine, false, 0, 154) == AreaState.NoEfficiency, w, "eff NaN → NoEfficiency");
            a = BaseArea(); a.RunsCounted = 0; Check(Rules.AreaStateOf(a, nine, false, 0, 154) == AreaState.NoOpenRun, w, "runs 0 → NoOpenRun");
            a = BaseArea(); a.SkiDayLodges = 0; Check(Rules.AreaStateOf(a, nine, false, 0, 154) == AreaState.NoLodge, w, "lodges 0 → NoLodge");
            a = BaseArea();
            Check(Rules.AreaStateOf(a, 0.1875f, false, 0, 154) == AreaState.OutOfHours, w, "04:30 → OutOfHours");
            Check(Rules.AreaStateOf(a, 0.6875f, false, 0, 154) == AreaState.OutOfHours, w, "16:30 day → OutOfHours");
            Check(Rules.AreaStateOf(a, 0.6875f, true, 0, 154) == AreaState.Open, w, "16:30 night → Open");
            Check(Rules.AreaStateOf(a, 0.875f, true, 0, 154) == AreaState.OutOfHours, w, "21:00 night → OutOfHours");
            Check(Rules.AreaStateOf(a, nine, false, 154, 154) == AreaState.Full, w, "claimed 154 of 154 → Full");
            Check(Rules.AreaStateOf(a, nine, false, 153, 154) == AreaState.Open, w, "claimed 153 of 154 → Open");
            Check(Rules.AreaStateOf(a, nine, false, 0, 0) == AreaState.Full, w, "capacity 0 → Full (s1-spec N9)");
            a = BaseArea(); a.ScoreKnown = false; a.WriteHeld = true; a.RunsCounted = 0;
            Check(Rules.AreaStateOf(a, 0.1875f, false, 999, 0) == AreaState.NoScore, w, "everything wrong → NoScore (order)");
            a = BaseArea(); a.SkiDayLodges = 0;
            Check(Rules.AreaStateOf(a, 0.1875f, false, 999, 0) == AreaState.NoLodge, w, "no lodge + night + full → NoLodge (order)");
            // Fixer pass (review TL-1): OutOfHours before Full (ch1 §3.3) — a full hill at night is OutOfHours.
            Check(Rules.AreaStateOf(BaseArea(), 0.1875f, false, 154, 154) == AreaState.OutOfHours, w, "04:30 + 154 of 154 → OutOfHours (order)");

            // HourOf / InClaimWindow.
            Check(Rules.HourOf(0.375f) == 9.0 && Rules.HourOf(0.5f) == 12.0 && Rules.HourOf(1.25f) == 6.0 && Rules.HourOf(0f) == 0.0, w,
                  "HourOf 09:00 / 12:00 / frac(1.25) 06:00 / 0");
            Check(double.IsNaN(Rules.HourOf(float.NaN)) && !Rules.InClaimWindow(float.NaN, true), w, "HourOf(NaN) NaN, not in the window");
            Check(Rules.InClaimWindow(0.21875f, false) && !Rules.InClaimWindow(0.20703125f, false), w, "05:15 in, 04:58 out");
            // O-7: 5/24 is not exact in float — the window opens one tick late (0.20833333f × 24 < 5).
            Check(!Rules.InClaimWindow(5f / 24f, false) && Rules.InClaimWindow(54614f / 262144f, false), w,
                  $"05:00 as a float (hour {R(Rules.HourOf(5f / 24f))}) is one tick early; tick 54,614 is in");
            Check(Rules.CloseHourOf(false) == 16.5 && Rules.CloseHourOf(true) == 21.0, w, "CloseHourOf 16.5 / 21");
        }

        // ================================================================== E. class, reach, distance, travel time (ch1 §3.4)

        private static void Travel()
        {
            const string w = "skiday E travel";
            Check(Rules.ClassOf(500, true, false, false) == TC.Walk && Rules.ClassOf(900, true, false, false) == TC.Walk, w, "children walk ≤ 900 m");
            Check(Rules.ClassOf(900.01, true, true, true) == TC.None, w, "a child beyond walking distance → None");
            Check(Rules.ClassOf(3360, false, true, false) == TC.Car && Rules.ClassOf(3360, false, false, true) == TC.Car, w, "parked car / free household car → Car");
            Check(Rules.ClassOf(3360, false, false, false) == TC.NoCarFar, w, "no car, far → NoCarFar");
            Check(Rules.ClassOf(double.NaN, false, true, true) == TC.None && Rules.ClassOf(-1, false, true, true) == TC.None, w, "NaN / −1 → None");
            Check(Rules.ClassOf(0, false, false, false) == TC.Walk && Rules.ClassOf(900.01, false, true, false) == TC.Car, w, "0 → Walk; 900.01 with a car → Car");

            Check(Rules.WithinReach(7000, 7), w, "7000 m at 7 km (inclusive)");
            Check(!Rules.WithinReach(7000.01, 7), w, "7000.01 m at 7 km");
            Check(Rules.WithinReach(12000, 99), w, "reach 99 clamps to 12");
            Check(!Rules.WithinReach(2000.5, 0), w, "reach 0 clamps to 2");
            Check(!Rules.WithinReach(double.NaN, 7), w, "NaN");

            double[] dfIn = { 1.0, 1.5, 3.36, 6.999, 7, 7.5 };
            double[] dfOut = { 1, 0.916666667, 0.606666667, 0.000166667, 0, 0 };
            for (int i = 0; i < dfIn.Length; i++)
            {
                double got = Rules.DistanceFactor(dfIn[i], 7);
                Check(Near(got, dfOut[i], 1e-9), w, $"DistanceFactor({dfIn[i]}, 7) = {R(got)}, expected {dfOut[i]}");
            }
            Check(Near(Rules.DistanceFactor(1.5, 2), 0.5, 1e-9), w, "DistanceFactor(1.5, 2) = 0.5");
            Check(Near(Rules.DistanceFactor(3.36, 12), 0.785454545, 1e-9), w, "DistanceFactor(3.36, 12) = 0.785454545");
            Check(Near(Rules.DistanceFactor(7, 12), 0.454545455, 1e-9), w, "DistanceFactor(7, 12) = 0.454545455");
            Check(Rules.DistanceFactor(double.NaN, 7) == 0 && Rules.DistanceFactor(-1, 7) == 0 && Rules.DistanceFactor(double.PositiveInfinity, 7) == 0,
                  w, "DistanceFactor(NaN | −1 | +∞, 7) = 0");
            Check(Rules.DistanceFactor(0, 7) == 1.0 && Rules.DistanceFactor(0.5, 0) == 1.0 && Rules.DistanceFactor(2, 0) == 0.0, w, "0 → 1; reach 0 clamps to 2");

            double[][] th =
            {
                new[] { 3360, 2.2, 1.985454545 }, new[] { 900, 0.3, 3.9 }, new[] { 500, 0.3, 2.166666667 },
                new[] { 3360, 1.2, 3.64 }, new[] { 5000, 2.2, 2.954545455 }, new[] { 2500, 2.2, 1.477272727 },
                new[] { 3360, 0, 436.8 }, new[] { 3360, double.NaN, 436.8 },
            };
            for (int i = 0; i < th.Length; i++)
            {
                double got = Rules.TravelHours(th[i][0], th[i][1]);
                Check(Near(got, th[i][2], 1e-9), w, $"TravelHours({th[i][0]}, {th[i][1]}) = {R(got)}, expected {th[i][2]}");
            }
            Check(double.IsPositiveInfinity(Rules.TravelHours(double.NaN, 2.2)) && double.IsPositiveInfinity(Rules.TravelHours(double.PositiveInfinity, 2.2)) &&
                  double.IsPositiveInfinity(Rules.TravelHours(-1, 2.2)), w, "TravelHours(NaN | +∞ | −1, 2.2) = +∞");

            Check(Rules.StartSpeedOf(TC.Walk) == 0.3 && Rules.StartSpeedOf(TC.Car) == 2.2 && Rules.StartSpeedOf(TC.NoCarFar) == 1.2 &&
                  Rules.StartSpeedOf(TC.None) == 0.3, w, "StartSpeedOf 0.3 / 2.2 / 1.2 / None = Walk");
            Check(Rules.MinSpeedOf(TC.Walk) == 0.1 && Rules.MinSpeedOf(TC.Car) == 0.5 && Rules.MinSpeedOf(TC.NoCarFar) == 0.2 &&
                  Rules.MinSpeedOf(TC.None) == 0.1, w, "MinSpeedOf 0.1 / 0.5 / 0.2 / None = Walk");
            Check(Rules.MaxSpeedOf(TC.Walk) == 1.0 && Rules.MaxSpeedOf(TC.Car) == 8.0 && Rules.MaxSpeedOf(TC.NoCarFar) == 8.0 &&
                  Rules.MaxSpeedOf(TC.None) == 1.0, w, "MaxSpeedOf 1 / 8 / 8 / None = Walk");
        }

        // ================================================================== F. hours and deadline (ch1 §3.5)

        private static void Deadline()
        {
            const string w = "skiday F deadline";
            float[] times = { 0.1875f, 0.25f, 0.375f, 0.4375f, 0.46875f, 0.5f, 0.53125f, 0.625f, 0.6875f, 0.75f, 0.84375f, 0.875f };
            long[] day = { -1, 114688, 81920, 65536, 57344, 49152, 40960, 16384, -1, -1, -1, -1 };
            long[] night = { -1, 163840, 131072, 114688, 106496, 98304, 90112, 65536, 49152, 32768, 8192, -1 };
            for (int i = 0; i < times.Length; i++)
            {
                for (int n = 0; n < 2; n++)
                {
                    long want = n == 0 ? day[i] : night[i];
                    bool ok = Rules.TryClosingFrame(Now, times[i], n == 1, out uint closing);
                    long got = ok ? (long)closing - Now : -1;
                    Check(got == want && (ok || closing == 0u), w + " closing",
                          $"t {times[i]} ({(n == 0 ? "day" : "night")}): closing − now {got}, expected {want} (−1 = false)");
                }
            }
            // Fixer pass (review TL-1): floor, not round (ch1 §3.5). Every time above is k/32, where both agree; 0.3f is
            // 0.300000011920928955078125, so left = (0.6875 − t) × 262,144 = 101,580.796875 exactly: floor 101,580.
            {
                bool ok = Rules.TryClosingFrame(Now, 0.3f, false, out uint closing);
                Check(ok && (long)closing - Now == 101580, w + " closing", $"t 0.3f (day): closing − now {(long)closing - Now}, expected 101,580 (floor)");
            }

            // ClaimFits (need = (t + 2) × 10,922.667).
            Check(Rules.ClaimFits(Now, Now + 100000, Now + 49152, 1.985454545), w, "FT8 car, closing 49,152 (12:00) → fits");
            Check(!Rules.ClaimFits(Now, Now + 100000, Now + 40960, 1.985454545), w, "FT8 car, closing 40,960 (12:45) → no");
            Check(!Rules.ClaimFits(Now, Now + 43531, Now + 81920, 1.985454545), w, "vanilla 43,531 < need 43,531.79 → no");
            Check(Rules.ClaimFits(Now, Now + 43532, Now + 81920, 1.985454545), w, "vanilla 43,532 → fits");
            Check(Rules.ClaimFits(Now, Now + 100000, Now + 65536, 3.9), w, "walk 900 m, closing 65,536 → fits (need 64,443.73)");
            Check(!Rules.ClaimFits(Now, Now + 100000, Now + 57344, 3.9), w, "walk 900 m, closing 57,344 → no");
            Check(!Rules.ClaimFits(Now, Now + 30000, Now + 81920, 2.166666667), w, "walk 500 m, vanilla 30,000 → no (vanilla)");
            Check(!Rules.ClaimFits(Now, Now, Now + 81920, 1.0), w, "vanilla = now → no");
            Check(!Rules.ClaimFits(Now, Now + 100000, Now, 0.0) && !Rules.ClaimFits(Now, Now - 5, Now + 81920, 0.0), w, "closing = now / vanilla before now → no");
            Check(!Rules.ClaimFits(Now, Now + 100000, Now + 81920, double.NaN), w, "t NaN → no");
            Check(!Rules.ClaimFits(Now, Now + 100000, Now + 81920, double.PositiveInfinity), w, "t +∞ → no");
            Check(!Rules.ClaimFits(Now, Now + 100000, Now + 81920, -1.0), w, "t −1 → no");
            Check(Rules.ClaimFits(Now, Now + 21846, Now + 21846, 0.0) && !Rules.ClaimFits(Now, Now + 21845, Now + 81920, 0.0), w,
                  "t 0: two hours = 21,845.33 frames (21,846 fits, 21,845 not)");

            Check(Rules.ClaimDeadline(1100000u, 1049152u) == 1049152u && Rules.ClaimDeadline(1049152u, 1100000u) == 1049152u, w,
                  "ClaimDeadline(1,100,000, 1,049,152) = 1,049,152 (either order)");
        }

        // ================================================================== G. chance oracles (ch1 §3.6)

        private static Rules.Knobs K(int locals = 25, int tourists = 50, int reach = 7, int powder = 100)
        {
            return new Rules.Knobs { LocalsPct = locals, TouristsPct = tourists, ReachKm = reach, PowderPct = powder };
        }

        private static void Chances()
        {
            const string w = "skiday G chance";
            Rules.Knobs def = K();
            var rows = new (string id, GK who, int a, double d, double o, double powder, int price, double conf, Rules.Knobs k, double p)[]
            {
                ("R1", GK.Resident, 75, 3.36, 1, 0, 40, 1, def, 0.11375),
                ("R2", GK.Resident, 75, 3.36, 1, 1, 40, 1, def, 0.2275),
                ("R3", GK.Resident, 75, 3.36, 1, 0, 100, 1, def, 0.079625),
                ("R15", GK.Resident, 75, 3.36, 1, 0, 150, 1, def, 0.0511875),
                ("R4", GK.Resident, 75, 3.36, 1, 0, 200, 1, def, 0.02275),
                ("R5", GK.Resident, 75, 3.36, 0, 1, 40, 1, def, 0),
                ("R6", GK.Resident, 75, 7, 1, 0, 40, 1, def, 0),
                ("R7", GK.Resident, 75, 0.5, 1, 0, 40, 1, def, 0.1875),
                ("R8", GK.Resident, 75, 3.36, 1, 0, 40, 0.1, def, 0.011375),
                ("R9", GK.Resident, 200, 0.5, 1, 0, 40, 1, def, 0.2),
                ("R10", GK.Resident, 200, 0.5, 1, 1, 0, 1, K(50, 100, 12, 200), 0.6),
                ("R11", GK.Resident, 75, 3.36, 0.5, 0.5, 40, 1, def, 0.0853125),
                ("R12", GK.Resident, 75, 3.36, 1, 0, 40, 1, K(reach: 12), 0.147272727),
                ("R13", GK.Resident, 75, 3.36, 1, 1, 40, 1, K(powder: 0), 0.11375),
                ("R14", GK.Resident, 75, 3.36, 1, 0, 40, 1, K(locals: 0), 0),
                ("T1", GK.Tourist, 75, 3.36, 1, 0, 40, 1, def, 0.11375),
                ("T2", GK.Tourist, 75, 3.36, 1, 1, 40, 1, def, 0.1421875),
                ("T3", GK.Tourist, 75, 3.36, 1, 0, 40, 0.1, def, 0.011375),
                ("T4", GK.Tourist, 200, 0.2, 1, 1, 40, 1, K(tourists: 100, powder: 200), 0.45),
                ("T5", GK.Tourist, 160, 0.3, 1, 0, 40, 1, def, 0.3),
                ("X1", GK.Resident, 75, double.NaN, double.NaN, double.NaN, -5, double.NaN, def, 0),
                ("X2", GK.Resident, -10, -1, -1, -1, 40, -1, K(-5, -5, -5, -5), 0),
                ("X3", GK.Resident, 75, double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity, 40, double.PositiveInfinity, def, 0),
                ("X4", GK.Resident, 900, 0.1, 5, 5, 40, 5, K(900, 900, 900, 900), 0.6),
            };
            for (int i = 0; i < rows.Length; i++)
            {
                var r = rows[i];
                double got = Rules.Chance(r.who, r.a, r.d, r.o, r.powder, r.price, r.conf, r.k);
                Check(Near(got, r.p, 1e-9), w + " " + r.id, $"p {R(got)}, expected {r.p}");
            }

            int[] priceIn = { -5, 0, 40, 100, 150, 200, 260 };
            double[] priceOut = { 1, 1, 1, 0.7, 0.45, 0.2, 0.2 };
            for (int i = 0; i < priceIn.Length; i++)
                Check(Near(Rules.PriceFactor(priceIn[i]), priceOut[i], 1e-12), w + " price", $"PriceFactor({priceIn[i]}) = {R(Rules.PriceFactor(priceIn[i]))}, expected {priceOut[i]}");
            Check(Rules.PriceFactor(40) == 1.0, w + " price", "PriceFactor(40) is exactly 1");

            Check(Rules.OpenShare(66.956700006, 133.913400012) == 0.5 && Rules.OpenShare(200, 100) == 1.0 && Rules.OpenShare(5, 0) == 0.0 &&
                  Rules.OpenShare(5, double.NaN) == 0.0 && Rules.OpenShare(5, double.PositiveInfinity) == 0.0 && Rules.OpenShare(double.NaN, 100) == 0.0 &&
                  Rules.OpenShare(-1, 100) == 0.0, w + " open", "OpenShare: half, > full → 1, full 0 / NaN / +∞ → 0, NaN / negative value → 0");
            Check(Near(Rules.SnowFactor(GK.Resident, 1, 1, 100), 2.0, 1e-12) && Near(Rules.SnowFactor(GK.Tourist, 1, 1, 100), 1.25, 1e-12) &&
                  Near(Rules.SnowFactor(GK.Resident, 0.5, 0.5, 200), 1.0, 1e-12) && Rules.SnowFactor(GK.Resident, 1, 1, 0) == 1.0 &&
                  Near(Rules.SnowFactor(GK.Resident, 1, 1, 999), 3.0, 1e-12) && Rules.SnowFactor(GK.Tourist, 0, 1, 200) == 0.0, w + " snow",
                  "SnowFactor: powder doubles residents at 100 %, × 1.25 tourists, pull 0 ignores, pull clamps 200, open 0 → 0");

            Rules.Knobs d0 = Rules.DefaultKnobs();
            Check(d0.LocalsPct == 25 && d0.TouristsPct == 50 && d0.ReachKm == 7 && d0.PowderPct == 100, w + " knobs", "DefaultKnobs 25 / 50 / 7 / 100");
            Rules.Knobs lo = Rules.ClampKnobs(K(-5, -5, -5, -5)), hi = Rules.ClampKnobs(K(900, 900, 900, 900)), same = Rules.ClampKnobs(K(30, 70, 9, 150));
            Check(lo.LocalsPct == 0 && lo.TouristsPct == 0 && lo.ReachKm == 2 && lo.PowderPct == 0, w + " knobs", "ClampKnobs(−5) → 0 / 0 / 2 / 0");
            Check(hi.LocalsPct == 50 && hi.TouristsPct == 100 && hi.ReachKm == 12 && hi.PowderPct == 200, w + " knobs", "ClampKnobs(900) → 50 / 100 / 12 / 200");
            Check(same.LocalsPct == 30 && same.TouristsPct == 70 && same.ReachKm == 9 && same.PowderPct == 150, w + " knobs", "ClampKnobs in range → unchanged");

            EntityRef s1 = new EntityRef(1, 1), s5 = new EntityRef(5, 1);
            Check(Rules.IsBetter(0.2, 900, s5, 0.1, 300, s1), w + " best", "higher p wins over a shorter distance and a lower sign");
            Check(Rules.IsBetter(0.1, 300, s5, 0.1, 900, s1), w + " best", "equal p: 300 beats 900");
            Check(Rules.IsBetter(0.1, 300, s1, 0.1, 300, s5), w + " best", "equal p and distance: s1 beats s5");
            Check(!Rules.IsBetter(0.1, 300, s5, 0.1, 300, s1), w + " best", "equal p and distance: s5 does not beat s1");
            Check(!Rules.IsBetter(0, 300, s1, 0, 900, s5), w + " best", "p 0 vs best 0 → false");
            Check(Rules.IsBetter(0.05, 900, s5, 0, 0, EntityRef.Null) && !Rules.IsBetter(double.NaN, 1, s1, 0, 0, EntityRef.Null) &&
                  !Rules.IsBetter(0.05, 300, s1, 0.1, 900, s5), w + " best", "first area (best 0) wins; NaN never; lower p never");
            Check(Rules.IsBetter(0.1, 300, new EntityRef(5, 1), 0.1, 300, new EntityRef(5, 2)), w + " best", "equal Index: lower Version wins");
        }

        // ================================================================== H. chance properties (ch1 §7 H)

        private static readonly int[] PropA = { 0, 10, 41, 75, 120, 160, 200 };
        private static readonly double[] PropO = { 0, 0.25, 0.5, 0.9, 1 };
        private static readonly double[] PropPowder = { 0, 1.0 / 6.0, 0.5, 0.8333, 1 };
        private static readonly int[] PropPrice = { 0, 20, 40, 60, 100, 150, 200 };
        private static readonly double[] PropConf = { 0.1, 0.3, 0.6, 1 };

        private struct ChanceIn
        {
            public GK Who; public int A; public double D, O, Powder; public int Price; public double Conf; public Rules.Knobs Knobs;
            public double P => Rules.Chance(Who, A, D, O, Powder, Price, Conf, Knobs);
            public override string ToString() =>
                $"{Who} A {A} d {R(D)} O {O} powder {R(Powder)} price {Price} conf {Conf} knobs {Knobs.LocalsPct}/{Knobs.TouristsPct}/{Knobs.ReachKm}/{Knobs.PowderPct}";
        }

        private static void ChanceProperties()
        {
            const string w = "skiday H chance property";
            const int Sets = 20000;
            const double Tol = 1e-12;   // two clamps of the same straight line can differ by an ulp; scratch saw none
            var rng = new System.Random(20261005);
            string[] names =
            {
                "A + 10 non-decreasing", "O + 0.1 non-decreasing", "powder + 0.1 non-decreasing", "PowderPct + 10 non-decreasing",
                "Locals/TouristsPct + 5 non-decreasing", "ReachKm + 1 non-decreasing", "conf + 0.1 non-decreasing",
                "d + 0.5 non-increasing", "price + 10 non-increasing", "O = 0 → 0", "conf = 0 → 0", "A = 0 → 0", "d ≥ reach → 0",
                "0 ≤ p ≤ 0.6 residents / 0.45 tourists",
            };
            int[] bad = new int[names.Length];
            string[] first = new string[names.Length];
            for (int n = 0; n < Sets; n++)
            {
                var c = new ChanceIn
                {
                    Who = rng.Next(2) == 0 ? GK.Resident : GK.Tourist,
                    A = PropA[rng.Next(PropA.Length)],
                    D = rng.NextDouble() * 13.0,
                    O = PropO[rng.Next(PropO.Length)],
                    Powder = PropPowder[rng.Next(PropPowder.Length)],
                    Price = PropPrice[rng.Next(PropPrice.Length)],
                    Conf = PropConf[rng.Next(PropConf.Length)],
                    Knobs = K(rng.Next(0, 51), rng.Next(0, 101), rng.Next(2, 13), rng.Next(0, 201)),
                };
                double p = c.P;
                ChanceIn m;
                m = c; m.A += 10; Up(0, p, m);
                m = c; m.O += 0.1; Up(1, p, m);
                m = c; m.Powder += 0.1; Up(2, p, m);
                m = c; m.Knobs.PowderPct += 10; Up(3, p, m);
                m = c; m.Knobs.LocalsPct += 5; m.Knobs.TouristsPct += 5; Up(4, p, m);
                m = c; m.Knobs.ReachKm += 1; Up(5, p, m);
                m = c; m.Conf += 0.1; Up(6, p, m);
                m = c; m.D += 0.5; Down(7, p, m);
                m = c; m.Price += 10; Down(8, p, m);
                m = c; m.O = 0; Zero(9, m);
                m = c; m.Conf = 0; Zero(10, m);
                m = c; m.A = 0; Zero(11, m);
                m = c; m.D = c.Knobs.ReachKm + rng.NextDouble() * 3.0; Zero(12, m);
                double max = c.Who == GK.Resident ? 0.6 : 0.45;
                if (!(p >= 0.0 && p <= max + Tol))
                    Note(13, c + $": p {R(p)}");
            }
            for (int i = 0; i < names.Length; i++)
                Check(bad[i] == 0, w, $"{names[i]}: {bad[i]} violation(s) in {Sets} sets{(first[i] != null ? "; first: " + first[i] : "")}");

            void Note(int i, string what)
            {
                if (bad[i]++ == 0)
                    first[i] = what;
            }
            void Up(int i, double before, ChanceIn after)
            {
                double q = after.P;
                if (!(q >= before - Tol))
                    Note(i, after + $": {R(before)} → {R(q)}");
            }
            void Down(int i, double before, ChanceIn after)
            {
                double q = after.P;
                if (!(q <= before + Tol))
                    Note(i, after + $": {R(before)} → {R(q)}");
            }
            void Zero(int i, ChanceIn after)
            {
                double q = after.P;
                if (q != 0.0)
                    Note(i, after + $": p {R(q)}");
            }
        }

        // ================================================================== I. rolls (ch1 §3.7)

        private static void Rolls()
        {
            const string w = "skiday I rolls";
            Check(Rules.ResidentRoll(1, 1, 5000007u) == 0.034786664364000464, w, "ResidentRoll(1, 1, 5,000,007) = " + R(Rules.ResidentRoll(1, 1, 5000007u)));
            Check(Rules.ResidentRoll(123456, 3, 9876543u) == 0.7031013458570818, w, "ResidentRoll(123456, 3, 9,876,543) = " + R(Rules.ResidentRoll(123456, 3, 9876543u)));
            Check(Rules.TouristRoll(777, 1, 24301) == 0.7941313215370011, w, "TouristRoll(777, 1, 24301) = " + R(Rules.TouristRoll(777, 1, 24301)));
            Check(Rules.TouristRoll(777, 1, 24302) == 0.5337007754813868, w, "TouristRoll(777, 1, 24302) = " + R(Rules.TouristRoll(777, 1, 24302)));
            Check(Rules.ResidentRoll(1, 1, 5000007u) == Rules.ResidentRoll(1, 1, 5000007u) && Rules.TouristRoll(777, 1, 24301) == Rules.TouristRoll(777, 1, 24301),
                  w, "same inputs, same roll");
            Check(Rules.ResidentRoll(1, 2, 5000007u) != Rules.ResidentRoll(1, 1, 5000007u) && Rules.TouristRoll(777, 1, 24301) != Rules.ResidentRoll(777, 1, 24301u),
                  w, "version and salt change the roll");

            bool mixSame = true;
            ulong[] mixIn = { 0UL, 1UL, 0x534B494441595331UL, ulong.MaxValue, 0x0123456789ABCDEFUL };
            for (int i = 0; i < mixIn.Length; i++)
                mixSame &= Rules.Mix64(mixIn[i]) == RunSnowRules.Mix64(mixIn[i]);
            Check(mixSame, w, "Mix64 = RunSnowRules.Mix64 (splitmix64 finalizer)");
            Check(Rules.KeyOf(1, 1) == 0x0000000100000001UL && Rules.KeyOf(-1, 2) == 0xFFFFFFFF00000002UL && Rules.KeyOf(5, -1) == 0x00000005FFFFFFFFUL,
                  w, "KeyOf packs (uint)Index << 32 | (uint)Version");
            Check(Rules.UnitOf(0UL) == 0.0 && Rules.UnitOf(ulong.MaxValue) == 9007199254740991.0 / 9007199254740992.0 && Rules.UnitOf(1UL << 63) == 0.5,
                  w, "UnitOf: 0, the largest value below 1, 0.5");
            Check(Rules.Hit(0.09, 0.1) && !Rules.Hit(0.1, 0.1) && !Rules.Hit(0.0, 0.0) && !Rules.Hit(0.5, double.NaN), w, "Hit = roll < p");

            // Statistics, i = 1..100,000.
            const int N = 100000;
            double[] ps = { 0.01, 0.11375, 0.5, 0.9 };
            int[] hitR = new int[ps.Length], hitT = new int[ps.Length];
            int[] binR = new int[20], binT = new int[20];
            int jointR = 0, jointT = 0;
            bool inRange = true;
            for (int i = 1; i <= N; i++)
            {
                uint f = (uint)(5000000 + 7 * i);
                double ur = Rules.ResidentRoll(i, 1, f), ur2 = Rules.ResidentRoll(i, 1, f + 1u);
                double ut = Rules.TouristRoll(i, 1, 24301), ut2 = Rules.TouristRoll(i, 1, 24302);
                inRange &= ur >= 0.0 && ur < 1.0 && ut >= 0.0 && ut < 1.0;
                for (int k = 0; k < ps.Length; k++)
                {
                    if (Rules.Hit(ur, ps[k])) hitR[k]++;
                    if (Rules.Hit(ut, ps[k])) hitT[k]++;
                }
                binR[(int)(ur * 20.0)]++;
                binT[(int)(ut * 20.0)]++;
                if (ur < 0.5 && ur2 < 0.5) jointR++;
                if (ut < 0.5 && ut2 < 0.5) jointT++;
            }
            Check(inRange, w, "every roll in [0, 1)");
            for (int k = 0; k < ps.Length; k++)
            {
                double sigma = Math.Sqrt(ps[k] * (1 - ps[k]) / N);
                double rr = hitR[k] / (double)N, rt = hitT[k] / (double)N;
                Check(Math.Abs(rr - ps[k]) <= 4 * sigma, w + " hit rate", $"resident p {ps[k]}: {R(rr)} ({R((rr - ps[k]) / sigma)}σ)");
                Check(Math.Abs(rt - ps[k]) <= 4 * sigma, w + " hit rate", $"tourist p {ps[k]}: {R(rt)} ({R((rt - ps[k]) / sigma)}σ)");
            }
            double chiR = Chi2(binR, N), chiT = Chi2(binT, N);
            Check(chiR <= 43.82, w + " chi2", $"resident χ² over 20 bins {R(chiR)} > 43.82 (99.9 %, 19 dof; scratch 23.50)");
            Check(chiT <= 43.82, w + " chi2", $"tourist χ² over 20 bins {R(chiT)} > 43.82 (99.9 %, 19 dof; scratch 12.68)");
            Check(Math.Abs(jointR / (double)N - 0.25) <= 0.00548, w + " joint", $"resident P(u < .5 at f and f + 1) = {R(jointR / (double)N)} (scratch 0.24899)");
            Check(Math.Abs(jointT / (double)N - 0.25) <= 0.00548, w + " joint", $"tourist P(u < .5 on day d and d + 1) = {R(jointT / (double)N)} (scratch 0.24923)");
        }

        private static double Chi2(int[] bins, int n)
        {
            double e = n / (double)bins.Length, chi = 0;
            for (int i = 0; i < bins.Length; i++)
                chi += (bins[i] - e) * (bins[i] - e) / e;
            return chi;
        }

        // ================================================================== J. back-off and speed (ch1 §3.8)

        private static void BackOff()
        {
            const string w = "skiday J backoff";
            Rules.ClassTrack car = Rules.NewTrack(TC.Car), walk = Rules.NewTrack(TC.Walk), far = Rules.NewTrack(TC.NoCarFar);
            Check(car.Arrived == 0 && car.Failed == 0 && car.Resolved == 0 && car.SpeedKmPerHour == 2.2 && walk.SpeedKmPerHour == 0.3 &&
                  far.SpeedKmPerHour == 1.2 && Rules.NewTrack(TC.None).SpeedKmPerHour == 0.3, w, "NewTrack: zeros, start speeds 2.2 / 0.3 / 1.2 / None 0.3");
            Check(Rules.Confidence(TC.Car, car) == 1.0 && Rules.Confidence(TC.Walk, walk) == 1.0 && Near(Rules.Confidence(TC.NoCarFar, far), 0.1, 1e-15), w,
                  "start confidence 1 / 1 / 0.1");
            Check(!Rules.UpdateCannotReach(ref car) && !car.CannotReachShown, w, "new car track: not shown (Resolved 0)");
            Check(Rules.PriorOf(TC.Walk) == 1.0 && Rules.PriorOf(TC.Car) == 1.0 && Rules.PriorOf(TC.NoCarFar) == 0.1 && Rules.PriorOf(TC.None) == 0.0, w,
                  "PriorOf 1 / 1 / 0.1 / 0");
            Check(Rules.Confidence(TC.None, Rules.NewTrack(TC.None)) == 0.1, w, "None's confidence floors at 0.1");

            // Car, 19 then 20 failures.
            car = Rules.NewTrack(TC.Car);
            for (int i = 0; i < 19; i++)
                Rules.RecordOutcome(ref car, false);
            Check(car.Arrived == 0 && Near(car.Failed, 15.274351, 1e-6) && Near(car.Failed, (1 - Math.Pow(0.975, 19)) / 0.025, 1e-9) && car.Resolved == 19, w,
                  $"19 failures: F {R(car.Failed)}, expected (1 − 0.975^19)/0.025 = 15.274351");
            Check(Near(Rules.Confidence(TC.Car, car), 0.207529679, 1e-9), w, $"19 failures: confidence {R(Rules.Confidence(TC.Car, car))}, expected 0.207529679");
            Check(!Rules.UpdateCannotReach(ref car), w, "19 failures: not shown (Resolved 19 < 20)");
            Rules.RecordOutcome(ref car, false);
            Check(car.Arrived == 0 && Near(car.Failed, 15.892492791, 1e-9) && car.Resolved == 20, w, $"20 failures: F {R(car.Failed)}, expected 15.892492791");
            Check(Near(Rules.Confidence(TC.Car, car), 0.201080882, 1e-9), w, $"20 failures: confidence {R(Rules.Confidence(TC.Car, car))}, expected 0.201080882");
            Check(Rules.UpdateCannotReach(ref car) && car.CannotReachShown, w, "20 failures: shown");
            // Then arrivals until cleared: still shown after 14 (conf < 0.6), cleared at 15 (0.604841618).
            int clearedAt = -1;
            double confAtClear = double.NaN;
            for (int i = 1; i <= 30 && clearedAt < 0; i++)
            {
                Rules.RecordOutcome(ref car, true);
                if (!Rules.UpdateCannotReach(ref car))
                {
                    clearedAt = i;
                    confAtClear = Rules.Confidence(TC.Car, car);
                }
            }
            Check(clearedAt == 15 && Near(confAtClear, 0.604841618, 1e-9), w, $"cleared after {clearedAt} arrival(s) at {R(confAtClear)}, expected 15 at 0.604841618");

            // Car, 7 of 20 arrive (i mod 3 = 0).
            car = Rules.NewTrack(TC.Car);
            for (int i = 0; i < 20; i++)
                Rules.RecordOutcome(ref car, i % 3 == 0);
            Check(Near(car.Arrived, 5.497218871, 1e-9) && Near(car.Failed, 10.395273920, 1e-9), w, $"7 of 20: A {R(car.Arrived)} F {R(car.Failed)}, expected 5.497218871 / 10.395273920");
            Check(Near(Rules.Confidence(TC.Car, car), 0.477427287, 1e-9) && Rules.UpdateCannotReach(ref car), w,
                  $"7 of 20: confidence {R(Rules.Confidence(TC.Car, car))} (expected 0.477427287), shown");
            // Car, 10 arrive then 10 fail.
            car = Rules.NewTrack(TC.Car);
            for (int i = 0; i < 20; i++)
                Rules.RecordOutcome(ref car, i < 10);
            Check(Near(car.Arrived, 6.945677625, 1e-9) && Near(car.Failed, 8.946815166, 1e-9), w, $"10 then 10: A {R(car.Arrived)} F {R(car.Failed)}, expected 6.945677625 / 8.946815166");
            Check(Near(Rules.Confidence(TC.Car, car), 0.550241628, 1e-9) && !Rules.UpdateCannotReach(ref car), w,
                  $"10 then 10: confidence {R(Rules.Confidence(TC.Car, car))} (expected 0.550241628), not shown (between 0.5 and 0.6, unchanged)");
            // Between the thresholds a shown row stays shown.
            car.CannotReachShown = true;
            Check(Rules.UpdateCannotReach(ref car), w, "confidence 0.55 keeps a shown row (hysteresis)");

            // NoCarFar arrivals and failures.
            far = Rules.NewTrack(TC.NoCarFar);
            double[] farConf = { 0.28, 0.397489540, 0.480191318, 0.541546366, 0.588861446 };
            for (int i = 0; i < farConf.Length; i++)
            {
                Rules.RecordOutcome(ref far, true);
                double c = Rules.Confidence(TC.NoCarFar, far);
                Check(Near(c, farConf[i], 1e-9), w, $"NoCarFar after {i + 1} arrival(s): {R(c)}, expected {farConf[i]}");
            }
            far = Rules.NewTrack(TC.NoCarFar);
            for (int i = 0; i < 5; i++)
                Rules.RecordOutcome(ref far, false);
            Check(Rules.Confidence(TC.NoCarFar, far) == 0.1, w, "NoCarFar, 5 failures: 0.1 (floor)");
            // Guards: NaN counts read 0; Resolved saturates.
            var odd = new Rules.ClassTrack { Arrived = double.NaN, Failed = double.NaN, Resolved = int.MaxValue };
            Check(Rules.Confidence(TC.Car, odd) == 1.0, w, "NaN counts read 0");
            Rules.RecordOutcome(ref odd, true);
            Check(odd.Resolved == int.MaxValue, w, "Resolved saturates at int.MaxValue");

            // LearnSpeed (Car, start 2.2, 3,360 m, claim 1,000,000).
            Rules.ClassTrack t = Rules.NewTrack(TC.Car);
            Rules.LearnSpeed(ref t, TC.Car, 3360, Now, Now + 21845u);
            Check(Near(t.SpeedKmPerHour, 2.196008331, 1e-9), w + " learn", $"+21,845: {R(t.SpeedKmPerHour)}, expected 2.196008331 (sample 2.184033)");
            t = Rules.NewTrack(TC.Car);
            Rules.LearnSpeed(ref t, TC.Car, 3360, Now, Now + 43690u);
            Check(Near(t.SpeedKmPerHour, 1.923004166, 1e-9), w + " learn", $"+43,690: {R(t.SpeedKmPerHour)}, expected 1.923004166");
            t = Rules.NewTrack(TC.Car);
            Rules.LearnSpeed(ref t, TC.Car, 3360, Now, Now + 1000u);
            Check(Near(t.SpeedKmPerHour, 3.65, 1e-12), w + " learn", $"+1,000: {R(t.SpeedKmPerHour)}, expected 3.65 (sample clamped to 8)");
            t = Rules.NewTrack(TC.Walk);
            Rules.LearnSpeed(ref t, TC.Walk, 250, Now, Now + 21845u);
            Check(t.SpeedKmPerHour == 0.3, w + " learn", "Walk 250 m (< 300 m) → unchanged 0.3");
            t = Rules.NewTrack(TC.Car);
            Rules.LearnSpeed(ref t, TC.Car, 3360, Now, Now);
            Rules.LearnSpeed(ref t, TC.Car, 3360, Now, Now - 100u);
            Rules.LearnSpeed(ref t, TC.Car, double.NaN, Now, Now + 21845u);
            Rules.LearnSpeed(ref t, TC.Car, 3360, Now, Now + 546u);   // 0.04999 h < 0.05
            Check(t.SpeedKmPerHour == 2.2, w + " learn", "arrival ≤ claim, NaN distance, under 0.05 h → unchanged");
            t = default;   // an unset speed starts from the class's start speed
            Rules.LearnSpeed(ref t, TC.Car, 3360, Now, Now + 21845u);
            Check(Near(t.SpeedKmPerHour, 2.196008331, 1e-9), w + " learn", "speed 0 starts from 2.2");
            t = Rules.NewTrack(TC.Walk);
            Rules.LearnSpeed(ref t, TC.Walk, 900, Now, Now + 100000u);   // 9.155 h → 0.128 km/h, clamp 0.1..1 keeps it
            Check(Near(t.SpeedKmPerHour, 0.3 + 0.25 * (0.9 * 1.3 / (100000 / Rules.FramesPerHour) - 0.3), 1e-12), w + " learn", "Walk sample inside its clamps");
        }

        // ================================================================== K. powder (ch1 §3.9)

        private static void Powder()
        {
            const string w = "skiday K powder";
            Check(Rules.IsSnowHour(0.2f, -2f, 1800f, 596.44f, 0.01f, 0f), w, "−2 °C ambient, top 1800 → local −14.0356: snow");
            Check(!Rules.IsSnowHour(0.2f, 8f, 1300f, 596.44f, 0.01f, 0f), w, "8 °C, top 1300 → +0.9644: no");
            Check(Rules.IsSnowHour(0.2f, 8f, 1500f, 596.44f, 0.01f, 0f), w, "8 °C, top 1500 → −1.0356: snow");
            Check(!Rules.IsSnowHour(0.09f, -2f, 1800f, 596.44f, 0.01f, 0f) && Rules.IsSnowHour(0.1f, -2f, 1800f, 596.44f, 0.01f, 0f), w, "precipitation 0.09 no, 0.1 yes");
            Check(!Rules.IsSnowHour(float.NaN, -2f, 1800f, 596.44f, 0.01f, 0f) && !Rules.IsSnowHour(0.2f, float.NaN, 1800f, 596.44f, 0.01f, 0f) &&
                  !Rules.IsSnowHour(0.2f, -2f, float.NaN, 596.44f, 0.01f, 0f), w, "NaN → no");
            // Bit parity with SnowLineSystem.GetLocalTemperature's float expression at the freezing edge.
            float amb = 3f, top = 900f, bas = 600f, lapse = 0.01f;
            float local = amb - (top - bas) * lapse;
            Check(Rules.IsSnowHour(0.2f, amb, top, bas, lapse, local) && !Rules.IsSnowHour(0.2f, amb, top, bas, lapse, local - 1e-4f), w,
                  "the local temperature is the float expression ambient − (top − base) × lapse");

            for (int h = -1; h <= 12; h++)
            {
                double want = h <= 4 ? 0.0 : (h >= 10 ? 1.0 : (h - 4) / 6.0);
                Check(Near(Rules.PowderOf(h), want, 1e-12), w, $"PowderOf({h}) = {R(Rules.PowderOf(h))}, expected {R(want)}");
            }
            Check(Rules.PowderOf(5) == 1.0 / 6.0 && Rules.PowderOf(7) == 0.5 && Rules.PowderOf(9) == 5.0 / 6.0, w, "sixths exactly");
            Check(Rules.PowderMark(Rules.PowderOf(7)) && !Rules.PowderMark(Rules.PowderOf(6)), w, "PowderMark at 7 snow-hours, not 6");
            bool markOk = true;
            for (int h = 0; h <= 24; h++)
                markOk &= Rules.PowderMark(Rules.PowderOf(h)) == (h >= 7);
            Check(markOk && !Rules.PowderMark(double.NaN), w, "PowderMark ⇔ 7 or more snow-hours (h 0..24); NaN no");

            Check(Rules.SnowHourSampleDate(0.5f, 0, 12) == 0.5f, w, "SnowHourSampleDate(0.5, 0, 12) = 0.5");
            Check(Near(Rules.SnowHourSampleDate(0.5f, 1, 12), 0.4965278, 1e-7), w, "SnowHourSampleDate(0.5, 1, 12) = " + Rules.SnowHourSampleDate(0.5f, 1, 12));
            Check(Near(Rules.SnowHourSampleDate(0f, 1, 12), 0.9965278, 1e-7), w, "SnowHourSampleDate(0, 1, 12) = " + Rules.SnowHourSampleDate(0f, 1, 12));
            Check(Near(Rules.SnowHourSampleDate(0f, 23, 12), 0.9201389, 1e-7), w, "SnowHourSampleDate(0, 23, 12) = " + Rules.SnowHourSampleDate(0f, 23, 12));

            // = the dates RunSnowRules.DayMeanC passes to its delegate, bit for bit.
            var rng = new System.Random(20261005);
            int[] dpys = { 1, 4, 12, 24 };
            var seen = new List<float>(24);
            int mismatches = 0, compared = 0;
            string firstBad = null;
            for (int n = 0; n < 2000; n++)
            {
                for (int d = 0; d < dpys.Length; d++)
                {
                    float aligned = RunSnowRules.HourAlignedDate((float)rng.NextDouble(), dpys[d], out int _);
                    seen.Clear();
                    RunSnowRules.DayMeanC(x => { seen.Add(x); return 0f; }, aligned, dpys[d]);
                    for (int k = 0; k < seen.Count; k++)
                    {
                        compared++;
                        float mine = Rules.SnowHourSampleDate(aligned, k, dpys[d]);
                        if (BitConverter.SingleToInt32Bits(mine) != BitConverter.SingleToInt32Bits(seen[k]) && mismatches++ == 0)
                            firstBad = $"aligned {aligned:R} dpy {dpys[d]} k {k}: {mine:R} vs {seen[k]:R}";
                    }
                }
            }
            Check(mismatches == 0 && compared == 2000 * 4 * 24, w, $"{mismatches} mismatch(es) in {compared} sample dates vs DayMeanC{(firstBad != null ? "; first " + firstBad : "")}");
        }

        // ================================================================== L. capacity (ch1 §3.10)

        private static void CapacityRows()
        {
            const string w = "skiday L capacity";
            var rows = new (double runs, int lodges, double powder, double cap, SkiDayLimit limit, int people)[]
            {
                (154.3, 1, 0, 154.3, SkiDayLimit.Runs, 154),
                (154.3, 1, 1, 192.875, SkiDayLimit.Runs, 193),
                (154.3, 1, 0.5, 173.5875, SkiDayLimit.Runs, 174),
                (105, 1, 0, 105, SkiDayLimit.Runs, 105),
                (2015, 3, 0, 1080, SkiDayLimit.Lodges, 1080),
                (2015, 3, 1, 1350, SkiDayLimit.Lodges, 1350),
                (745, 2, 0, 720, SkiDayLimit.Lodges, 720),
                (720, 2, 0, 720, SkiDayLimit.Runs, 720),
                (500, 0, 0, 0, SkiDayLimit.Lodges, 0),
                (0, 2, 1, 0, SkiDayLimit.Runs, 0),
                (double.NaN, 1, 0, 0, SkiDayLimit.Runs, 0),
                (154.3, 1, double.NaN, 154.3, SkiDayLimit.Runs, 154),
                (1e12, 5, 1, 2250, SkiDayLimit.Lodges, 2250),
            };
            for (int i = 0; i < rows.Length; i++)
            {
                var r = rows[i];
                double cap = Rules.Capacity(r.runs, r.lodges, r.powder, out SkiDayLimit limit);
                int people = Rules.CapacityPeople(cap);
                Check(Near(cap, r.cap, 1e-9) && limit == r.limit && people == r.people, w,
                      $"Capacity({r.runs}, {r.lodges}, {r.powder}) = {R(cap)} {limit} → {people}, expected {r.cap} {r.limit} → {r.people}");
            }
            Check(Rules.CapacityPeople(double.NaN) == 0 && Rules.CapacityPeople(-5) == 0 && Rules.CapacityPeople(0.4) == 0 && Rules.CapacityPeople(0.5) == 1 &&
                  Rules.CapacityPeople(1e12) == 1000000 && Rules.CapacityPeople(double.PositiveInfinity) == 1000000, w,
                  "CapacityPeople: NaN / −5 / 0.4 → 0, 0.5 → 1, 1e12 / +∞ → 1,000,000");
            Check(Rules.HasRoom(153, 154) && !Rules.HasRoom(154, 154) && !Rules.HasRoom(0, 0), w, "HasRoom(153, 154) T, (154, 154) F, (0, 0) F");

            // Properties (20,000 random).
            var rng = new System.Random(20261005);
            int incRuns = 0, incLodges = 0, incPowder = 0, limitRule = 0, bounds = 0;
            for (int n = 0; n < 20000; n++)
            {
                double runs = rng.NextDouble() * 3000.0;
                int lodges = rng.Next(0, 9);
                double powder = rng.Next(0, 7) / 6.0;
                double cap = Rules.Capacity(runs, lodges, powder, out SkiDayLimit limit);
                if (Rules.Capacity(runs + rng.NextDouble() * 200.0, lodges, powder, out SkiDayLimit _) < cap) incRuns++;
                if (Rules.Capacity(runs, lodges + 1, powder, out SkiDayLimit _) < cap) incLodges++;
                if (Rules.Capacity(runs, lodges, Math.Min(1.0, powder + 1.0 / 6.0), out SkiDayLimit _) < cap) incPowder++;
                double lodgeRoom = lodges * 360.0;
                if ((limit == SkiDayLimit.Runs) != (lodges > 0 && runs <= lodgeRoom)) limitRule++;
                if (!(cap >= 0.0 && cap <= 1.25 * Math.Min(runs, lodgeRoom))) bounds++;
            }
            Check(incRuns == 0, w + " property", $"non-decreasing in runs: {incRuns} violation(s)");
            Check(incLodges == 0, w + " property", $"non-decreasing in lodges: {incLodges} violation(s)");
            Check(incPowder == 0, w + " property", $"non-decreasing in powder: {incPowder} violation(s)");
            Check(limitRule == 0, w + " property", $"limit = Runs ⇔ lodges > 0 and runs ≤ lodges: {limitRule} violation(s)");
            Check(bounds == 0, w + " property", $"0 ≤ cap ≤ 1.25 × min(runs, lodges): {bounds} violation(s)");
        }

        // ================================================================== M. slices and ARM (ch1 §3.11)

        private static void Slices()
        {
            const string w = "skiday M slices";
            var rows = new (uint frame, uint ls, bool cbsRuns, uint cbs, uint[] skip)[]
            {
                (2368u, 5u, false, 4u, new[] { 5u }),
                (2379u, 5u, true, 4u, new[] { 4u, 5u }),
                (2443u, 6u, true, 8u, new[] { 6u, 8u }),
                (11u, 0u, true, 0u, new[] { 0u }),
                (971u, 15u, true, 12u, new[] { 12u, 15u }),
            };
            for (int i = 0; i < rows.Length; i++)
            {
                var r = rows[i];
                Check(Rules.SliceOf(r.frame, 64) == r.ls && Rules.RunsThisFrame(r.frame, 16, 11) == r.cbsRuns && Rules.SliceOf(r.frame, 16) == r.cbs, w,
                      $"frame {r.frame}: LS slice {Rules.SliceOf(r.frame, 64)}, CBS runs {Rules.RunsThisFrame(r.frame, 16, 11)}, CBS slice {Rules.SliceOf(r.frame, 16)}");
                for (uint s = 0; s < 16; s++)
                {
                    bool want = Array.IndexOf(r.skip, s) >= 0;
                    Check(Rules.SkipSlice(s, r.frame, 64, 16, 11) == want, w + " skip", $"frame {r.frame} slice {s}: expected {want}");
                }
            }
            Check(Rules.SliceOf(1000u, 0) == 0u && Rules.SliceOf(1000u, -64) == 0u, w, "SliceOf(1000, 0) = SliceOf(1000, −64) = 0 (guard)");
            Check(Rules.SliceOf(1024u * 16u + 64u, 64) == 1u && Rules.SliceOf(uint.MaxValue, 64) == 15u, w, "SliceOf wraps by & 15");

            Check(Rules.ArmCheck(64, 16, 16, 11) == ArmVerdict.Ok, w + " arm", "(64, 16, 16, 11) Ok");
            Check(Rules.ArmCheck(128, 16, 16, 0) == ArmVerdict.Ok, w + " arm", "(128, 16, 16, 0) Ok");
            Check(Rules.ArmCheck(32, 16, 16, 11) == ArmVerdict.LeisureTooFast, w + " arm", "(32, 16, 16, 11) LeisureTooFast");
            Check(Rules.ArmCheck(64, 32, 16, 11) == ArmVerdict.LeisureTooFast, w + " arm", "(64, 32, 16, 11) LeisureTooFast");
            Check(Rules.ArmCheck(64, 16, 16, 16) == ArmVerdict.BadOffset, w + " arm", "(64, 16, 16, 16) BadOffset");
            Check(Rules.ArmCheck(64, 16, 16, -1) == ArmVerdict.BadOffset, w + " arm", "(64, 16, 16, −1) BadOffset");
            Check(Rules.ArmCheck(0, 16, 16, 11) == ArmVerdict.BadInterval, w + " arm", "(0, 16, 16, 11) BadInterval");
            Check(Rules.ArmCheck(48, 16, 16, 11) == ArmVerdict.BadInterval, w + " arm", "(48, 16, 16, 11) BadInterval");
            Check(Rules.ArmCheck(64, 16, 8, 3) == ArmVerdict.BehaviorTooFast, w + " arm", "(64, 16, 8, 3) BehaviorTooFast (8 ≤ 8)");
            Check(Rules.ArmCheck(1 << 30, 1 << 28, 16, 11) == ArmVerdict.Ok && Rules.ArmCheck(1 << 30, 1 << 29, 16, 11) == ArmVerdict.LeisureTooFast, w + " arm",
                  "4 × trip in long (no overflow at 2^29)");
            Check(!Rules.IsPowerOfTwo(0) && Rules.IsPowerOfTwo(1) && Rules.IsPowerOfTwo(64) && !Rules.IsPowerOfTwo(48) && !Rules.IsPowerOfTwo(-64) &&
                  !Rules.IsPowerOfTwo(int.MinValue) && Rules.IsPowerOfTwo(1 << 30), w, "IsPowerOfTwo");
        }

        // ================================================================== N. day key and month step (ch1 §3.12)

        private static void DayAndMonth()
        {
            const string w = "skiday N day";
            Check(Rules.DayKey(2026, 0.5f, 12) == 24318 && Rules.DayKey(2026, 0.5f, 12) == SkiMoneyRules.MonthKey(2026, 0.5f), w, "DayKey(2026, 0.5, 12) = 24318 = MonthKey");
            Check(Rules.DayKey(2026, 0.99999f, 12) == 24323, w, "DayKey(2026, 0.99999, 12) = 24323");
            Check(Rules.DayKey(2026, 0.5f, 1) == 2026, w, "DayKey(2026, 0.5, 1) = 2026");
            Check(Rules.DayKey(2026, float.NaN, 12) == 24312, w, "DayKey(2026, NaN, 12) = 24312");
            Check(Rules.DayKey(2026, 0.5f, 0) == 2026, w, "DayKey(2026, 0.5, 0) = 2026 (days ≥ 1)");
            Check(Rules.DayKey(2026, 1.2f, 12) == 24323, w, "DayKey(2026, 1.2, 12) = 24323 (clamped)");
            var rng = new System.Random(20261005);
            int bad = 0;
            for (int n = 0; n < 100000; n++)
            {
                int year = rng.Next(2000, 2100);
                float date = (float)rng.NextDouble();
                if (Rules.DayKey(year, date, 12) != SkiMoneyRules.MonthKey(year, date))
                    bad++;
            }
            Check(bad == 0, w, $"DayKey = SkiMoneyRules.MonthKey at 12 days a year: {bad} mismatch(es) in 100,000");

            // Month step: the ledger's rule on the flags.
            const string m = "skiday N month";
            Rules.MonthFlags f0 = Rules.NewMonthFlags();
            Check(f0.Key == Rules.NoMonth && !f0.ThisSinceLoad && !f0.LastKnown && !f0.LastSinceLoad, m, "NewMonthFlags: NoMonth, flags false");
            Rules.MonthFlags f = Rules.NewMonthFlags();
            Check(Rules.Advance(ref f, 100) == MonthStep.Started && f.Key == 100 && f.ThisSinceLoad && !f.LastKnown, m, "first key → Started, ThisSinceLoad");
            Check(Rules.Advance(ref f, 100) == MonthStep.None && f.ThisSinceLoad, m, "same key → None");
            Check(Rules.Advance(ref f, 101) == MonthStep.Rolled && f.LastKnown && f.LastSinceLoad && !f.ThisSinceLoad, m, "+1 → Rolled, last month since load");
            Check(Rules.Advance(ref f, 102) == MonthStep.Rolled && f.LastKnown && !f.LastSinceLoad && !f.ThisSinceLoad, m, "+1 again → a full last month");
            Check(Rules.Advance(ref f, 110) == MonthStep.Jumped && !f.LastKnown && !f.LastSinceLoad && f.ThisSinceLoad && f.Key == 110, m, "jump → Jumped, restarts as after a load");
            f = Rules.NewMonthFlags();
            Rules.Advance(ref f, int.MaxValue - 1);
            Check(Rules.Advance(ref f, int.MaxValue) == MonthStep.Rolled && Rules.Advance(ref f, 5) == MonthStep.Jumped, m, "int.MaxValue rolls in, never out");

            int mismatches = 0;
            string first = null;
            for (int s = 0; s < 2000; s++)
            {
                var ledger = new SkiResortLedger();
                Rules.MonthFlags flags = Rules.NewMonthFlags();
                int key = rng.Next(20000, 30000);
                for (int step = 0; step < 50; step++)
                {
                    int pick = rng.Next(3);
                    if (step > 0)
                        key = pick == 0 ? key : (pick == 1 ? key + 1 : rng.Next(20000, 30000));
                    byte a = (byte)ledger.Advance(key);
                    byte b = (byte)Rules.Advance(ref flags, key);
                    bool same = a == b && ledger.MonthKey == flags.Key && ledger.ThisMonthSinceLoad == flags.ThisSinceLoad &&
                                ledger.LastMonthKnown == flags.LastKnown && ledger.LastMonthSinceLoad == flags.LastSinceLoad;
                    if (!same && mismatches++ == 0)
                        first = $"sequence {s} step {step} key {key}: ledger {a} vs flags {b}";
                }
            }
            Check(mismatches == 0, m, $"Advance(ref MonthFlags) = SkiResortLedger.Advance over 2,000 × 50 steps: {mismatches} mismatch(es){(first != null ? "; first " + first : "")}");
        }

        // ================================================================== O. slope-side geometry (ch1 §3.2)

        private static float PolygonDistance(float2 p, float2[] nodes)
        {
            bool inside = false;
            float e = float.PositiveInfinity;
            for (int i = 0; i < nodes.Length; i++)
            {
                float2 a = nodes[i], b = nodes[(i + 1) % nodes.Length];
                if (Rules.CrossesRayXZ(p, a, b))
                    inside = !inside;
                e = Rules.NearestOf(e, Rules.DistanceToSegmentXZ(p, a, b));
            }
            return inside ? 0f : e;
        }

        private static void Geometry()
        {
            const string w = "skiday O geometry";
            float2 bMin = new float2(0f, 0f), bMax = new float2(10f, 10f);
            Check(Rules.DistanceToBoundsXZ(new float2(5f, 5f), bMin, bMax) == 0f, w, "inside the box → 0");
            float corner = Rules.DistanceToBoundsXZ(new float2(-600f, -480f), bMin, bMax);
            Check(Near(corner, 768.375, 0.01), w, $"corner (−600, −480) = {corner} (√(600² + 480²) = 768.375)");
            float side = Rules.DistanceToBoundsXZ(new float2(810f, 5f), bMin, bMax);
            Check(side == 800f && Rules.IsSlopeside(side), w, $"(810, 5) → {side}, slope-side");
            float nan = Rules.DistanceToBoundsXZ(new float2(float.NaN, 5f), bMin, bMax);
            Check(float.IsPositiveInfinity(nan) && !Rules.IsSlopeside(nan), w, "NaN point → +∞, not slope-side");
            Check(float.IsPositiveInfinity(Rules.DistanceToBoundsXZ(new float2(5f, 5f), new float2(float.NegativeInfinity, 0f), bMax)) &&
                  float.IsPositiveInfinity(Rules.DistanceToBoundsXZ(new float2(5f, 5f), bMin, new float2(10f, float.NaN))), w, "non-finite bounds → +∞");
            float signs = Rules.DistanceXZ(new float2(914f, 3090f), new float2(-948f, 296f));
            Check(Near(signs, 3357.60, 0.01), w, $"the FT8 signs: {signs} (√(1862² + 2794²) = 3,357.60)");
            Check(float.IsPositiveInfinity(Rules.NearestOf(float.PositiveInfinity, float.NaN)) && Rules.NearestOf(900f, 750f) == 750f &&
                  Rules.NearestOf(750f, 900f) == 750f, w, "NearestOf(+∞, NaN) = +∞; (900, 750) = 750");

            float seg = Rules.DistanceToSegmentXZ(new float2(2214f, 0f), new float2(0f, 0f), new float2(1414f, 1414f));
            Check(Near(seg, 1565.534, 0.01), w, $"segment (2214, 0) to (0,0)-(1414,1414) = {seg} (1107·√2 = 1565.534)");
            Check(Near(Rules.DistanceToSegmentXZ(new float2(150f, 50f), new float2(100f, 0f), new float2(100f, 100f)), 50, 0.01), w, "(150, 50) to x = 100 → 50");
            Check(Near(Rules.DistanceToSegmentXZ(new float2(130f, 140f), new float2(100f, 100f), new float2(0f, 100f)), 50, 0.01), w, "(130, 140) to the end (100, 100) → 50");
            Check(Near(Rules.DistanceToSegmentXZ(new float2(5f, 5f), new float2(3f, 4f), new float2(3f, 4f)), 2.236068, 0.01), w, "degenerate segment → √5");
            Check(Rules.CrossesRayXZ(new float2(50f, 50f), new float2(100f, 0f), new float2(100f, 100f)), w, "(50, 50) crosses x = 100");
            Check(!Rules.CrossesRayXZ(new float2(150f, 50f), new float2(100f, 0f), new float2(100f, 100f)), w, "(150, 50) does not");
            Check(!Rules.CrossesRayXZ(new float2(50f, 50f), new float2(0f, 100f), new float2(0f, 0f)), w, "(50, 50) vs x = 0: 50 < 0 fails");
            Check(!Rules.CrossesRayXZ(new float2(50f, 50f), new float2(0f, 0f), new float2(100f, 0f)) &&
                  !Rules.CrossesRayXZ(new float2(float.NaN, 50f), new float2(100f, 0f), new float2(100f, 100f)), w, "a horizontal edge and NaN never cross");

            float2[] square = { new float2(0f, 0f), new float2(100f, 0f), new float2(100f, 100f), new float2(0f, 100f) };
            Check(PolygonDistance(new float2(50f, 50f), square) == 0f, w + " polygon", "square: (50, 50) inside → 0");
            Check(Near(PolygonDistance(new float2(150f, 50f), square), 50, 0.01), w + " polygon", "square: (150, 50) → 50");
            Check(Near(PolygonDistance(new float2(130f, 140f), square), 50, 0.01), w + " polygon", "square: (130, 140) → 50");
            Check(Near(PolygonDistance(new float2(-30f, 50f), square), 30, 0.01), w + " polygon", "square: (−30, 50) → 30");

            // The R-3 strip: a long diagonal run whose box admits a lodge the polygon refuses.
            float2[] strip = { new float2(0f, 0f), new float2(10f, -10f), new float2(1424f, 1404f), new float2(1414f, 1414f) };
            float2 lodge = new float2(2214f, 0f);
            float box = Rules.DistanceToBoundsXZ(lodge, new float2(0f, -10f), new float2(1424f, 1414f));
            float poly = PolygonDistance(lodge, strip);
            Check(box == 790f, w + " strip", $"box distance {box}, expected 790 (the old rule would pass it)");
            Check(Near(poly, 1551.392, 0.01), w + " strip", $"polygon distance {poly}, expected 1097·√2 = 1551.392");
            Check(Rules.CheckLodge(GoodLodge(box)) == LodgeReject.None && Rules.CheckLodge(GoodLodge(poly)) == LodgeReject.NotSlopeside, w + " strip",
                  "box → None (the hole R-3 closed), polygon → NotSlopeside");
            Check(PolygonDistance(new float2(712f, 702f), strip) == 0f, w + " strip", "(712, 702) on the centre line → 0");
            float near = PolygonDistance(new float2(1000f, 900f), strip);
            Check(Near(near, 56.569, 0.01) && Rules.DistanceToBoundsXZ(new float2(1000f, 900f), new float2(0f, -10f), new float2(1424f, 1414f)) == 0f,
                  w + " strip", $"(1000, 900) → {near} (80/√2 = 56.569), box 0");
        }

        // ================================================================== P. F2 power (ch1 §5)

        private static void Power()
        {
            const string w = "skiday P power";
            const LiftPower On = LiftPower.Powered, Off = LiftPower.NoPower, Unk = LiftPower.Unknown;
            Check(LiftServedRule.HalfPowerDivisor == 2 && LiftServedRule.BackPowerNumerator == 3 && LiftServedRule.BackPowerDenominator == 4 &&
                  LiftServedRule.FlagConnected == 1 && LiftServedRule.FlagNoElectricityWarning == 2 && LiftServedRule.FlagBottleneckWarning == 4, w,
                  "constants 2 / 3 / 4 and flags 1 / 2 / 4");
            var rows = new (string id, byte flags, int wanted, int fulfilled, LiftPower last, LiftPower expect)[]
            {
                ("no warning: locked electricity", 0, 1996, 0, On, On),
                ("FT8 lift 1, 90.9 %", 4, 1996, 1815, Unk, On),
                ("FT8 lift 2", 2, 1996, 1814, On, On),
                ("exactly half", 2, 1996, 998, On, On),
                ("under half", 2, 1996, 997, On, Off),
                ("nothing arrives", 2, 1996, 0, On, Off),
                ("both warnings, 999 of 2000", 6, 2000, 999, On, Off),
                ("both warnings, 1000 of 2000", 6, 2000, 1000, On, On),
                ("wanted 0", 4, 0, 0, On, On),
                ("wanted negative", 2, -5, -10, On, On),
                ("int.MaxValue, 1073741823", 2, int.MaxValue, 1073741823, On, Off),
                ("int.MaxValue, 1073741824 (no overflow)", 2, int.MaxValue, 1073741824, Unk, On),
                ("unknown, under half", 2, 1996, 997, Unk, Off),
                ("unknown, 1497 (half rule, not three quarters)", 2, 1996, 1497, Unk, On),
                // Hysteresis: last = NoPower needs three quarters.
                ("back: 1496 (5,984 < 5,988)", 2, 1996, 1496, Off, Off),
                ("back: 1497 (5,988)", 2, 1996, 1497, Off, On),
                ("back: 997", 2, 1996, 997, Off, Off),
                ("back: warning cleared", 0, 1996, 0, Off, On),
                ("back: 1499 of 2000", 2, 2000, 1499, Off, Off),
                ("back: 1500 of 2000", 2, 2000, 1500, Off, On),
                ("back: wanted 0", 2, 0, 0, Off, On),
                ("back: int.MaxValue, 1610612735", 2, int.MaxValue, 1610612735, Off, Off),
                ("back: int.MaxValue, 1610612736", 2, int.MaxValue, 1610612736, Off, On),
            };
            for (int i = 0; i < rows.Length; i++)
            {
                var r = rows[i];
                LiftPower got = LiftServedRule.PowerOf(r.flags, r.wanted, r.fulfilled, r.last);
                Check(got == r.expect, w, $"{r.id}: PowerOf({r.flags}, {r.wanted}, {r.fulfilled}, {r.last}) = {got}, expected {r.expect}");
            }
            Check(LiftServedRule.PowerOf(0) == On && LiftServedRule.PowerOf(1) == On && LiftServedRule.PowerOf(2) == Off && LiftServedRule.PowerOf(3) == Off &&
                  LiftServedRule.PowerOf(4) == Off && LiftServedRule.PowerOf(5) == Off, w, "the 1-argument overload: 0/1 → Powered, 2-5 → NoPower");
        }

        // ================================================================== Q. score additions (ch1 §4)

        private const byte Gr = Score.KindGroomed, Sm = Score.KindSnowmaking, Ni = Score.KindNight, Na = Score.KindNatural;
        private const byte Flat = Score.BandFlat, Green = Score.BandGreen, Blue = Score.BandBlue, Black = Score.BandBlack, DBlack = Score.BandDoubleBlack;

        private static Score.RunInput Run(byte kind, byte band, double areaM2, double topM, double bottomM)
        {
            return new Score.RunInput
            {
                Kind = kind, State = Score.BandState.Rated, Band = band, AreaM2 = areaM2, Served = true, OpenFactor = 1.0,
                TopM = topM, BottomM = bottomM,
            };
        }

        private static void Add(List<Score.RunInput> runs, int count, Score.RunInput run)
        {
            for (int i = 0; i < count; i++)
                runs.Add(run);
        }

        // The ScoreTests compositions (econ-round ch1 §3.1), re-built here: ScoreTests keeps its builders private.
        private static List<Score.RunInput> Small()
        {
            return new List<Score.RunInput> { Run(Gr, Green, 20000, 1150, 1000), Run(Gr, Blue, 25000, 1250, 1000), Run(Gr, Blue, 20000, 1250, 1050) };
        }

        private static List<Score.RunInput> Medium()
        {
            var runs = new List<Score.RunInput>();
            Add(runs, 3, Run(Gr, Green, 25000, 1250, 1000));
            Add(runs, 3, Run(Gr, Blue, 30000, 1400, 1000));
            Add(runs, 1, Run(Sm, Blue, 30000, 1400, 1000));
            Add(runs, 1, Run(Ni, Blue, 25000, 1300, 1000));
            Add(runs, 2, Run(Na, Black, 30000, 1500, 1100));
            return runs;
        }

        private static List<Score.RunInput> Large()
        {
            var runs = new List<Score.RunInput>();
            Add(runs, 6, Run(Gr, Green, 30000, 1300, 1000));
            Add(runs, 5, Run(Gr, Blue, 40000, 1500, 1000));
            Add(runs, 3, Run(Sm, Blue, 40000, 1500, 1000));
            Add(runs, 2, Run(Ni, Blue, 30000, 1300, 1000));
            Add(runs, 5, Run(Na, Black, 40000, 1800, 1200));
            Add(runs, 1, Run(Sm, Black, 40000, 1700, 1100));
            Add(runs, 3, Run(Na, DBlack, 40000, 1800, 1300));
            return runs;
        }

        /// <summary>The FT8 hill (ch1 §8): groomed black 25,100 + 25,100 + 25,000 m², natural blue 26,000, natural double
        /// black 24,000, all served and open. Heights are not in the log; these give the §7 Q top of 1,103 m.</summary>
        private static List<Score.RunInput> Ft8()
        {
            return new List<Score.RunInput>
            {
                Run(Gr, Black, 25100, 1103, 860), Run(Gr, Black, 25100, 1090, 860), Run(Gr, Black, 25000, 1080, 870),
                Run(Na, Blue, 26000, 1060, 880), Run(Na, DBlack, 24000, 1095, 900),
            };
        }

        private static List<Score.LiftInput> Lifts(int count)
        {
            var lifts = new List<Score.LiftInput>();
            for (int i = 0; i < count; i++)
                lifts.Add(new Score.LiftInput { Complete = true, Powered = true });
            return lifts;
        }

        private static Score.AreaInput AreaIn(int lodges, int backcountry = 40, bool gating = true)
        {
            return new Score.AreaInput
            {
                ActiveLodges = lodges, Efficiency = 1.0,
                Settings = new Score.ScoreSettings { K = 4, BackcountryPercent = backcountry, SnowGating = gating },
            };
        }

        private static List<Score.RunInput> Modify(List<Score.RunInput> runs, Func<Score.RunInput, int, Score.RunInput> f)
        {
            var copy = new List<Score.RunInput>(runs.Count);
            for (int i = 0; i < runs.Count; i++)
                copy.Add(f(runs[i], i));
            return copy;
        }

        private static void ScoreRow(string id, Score.AreaInput area, List<Score.RunInput> runs, int lifts, double full, double capacity,
            int night, bool topKnown, double topM, double openShare)
        {
            string w = "skiday Q score " + id;
            var parts = new List<Score.RunPart>();
            Score.Result r = Score.Compute(area, runs, Lifts(lifts), parts);
            Check(Near(r.RunsValueFull, full, 1e-6), w, $"RunsValueFull {R(r.RunsValueFull)}, expected {full}");
            Check(Near(r.RunsCapacity, capacity, 1e-9), w, $"RunsCapacity {R(r.RunsCapacity)}, expected {capacity}");
            Check(r.NightRunsCounted == night, w, $"NightRunsCounted {r.NightRunsCounted}, expected {night}");
            Check(r.TopKnown == topKnown && r.TopM == topM, w, $"TopKnown {r.TopKnown} TopM {R(r.TopM)}, expected {topKnown} {topM}");
            double share = Rules.OpenShare(r.RunsValue, r.RunsValueFull);
            Check(Near(share, openShare, 1e-9), w, $"OpenShare {R(share)}, expected {openShare}");
        }

        private static void ScoreAdditions()
        {
            const string w = "skiday Q score";
            Check(Score.SkiersPerHaGreen == 45.0 && Score.SkiersPerHaBlue == 25.0 && Score.SkiersPerHaBlack == 15.0 && Score.SkiersPerHaDoubleBlack == 7.5 &&
                  Score.SkiersPerHaUnrateable == 15.0 && Score.NaturalSkiersFactor == 0.5 && Score.MaxCapacityAreaM2 == 20000000.0, w,
                  "constants 45 / 25 / 15 / 7.5 / 15, natural 0.5, 2,000 ha");
            double[] perHa = { 0, 45, 25, 15, 7.5 };
            bool perHaOk = Score.SkiersPerHa(Score.BandState.Rated, 9) == 0.0;
            for (byte b = 0; b <= 4; b++)
                perHaOk &= Score.SkiersPerHa(Score.BandState.Rated, b) == perHa[b] && Score.SkiersPerHa(Score.BandState.Unrateable, b) == 15.0 &&
                           Score.SkiersPerHa(Score.BandState.Pending, b) == 0.0;
            Check(perHaOk, w, "SkiersPerHa: rated 0 / 45 / 25 / 15 / 7.5, out of range 0, unrateable 15, pending 0");
            var counted = new Score.RunPart { ServedFactor = 0.4, OpenFactor = 0.5, Counted = true };
            Check(Score.RunSkiersPerDay(Run(Na, Black, 30000, 0, 0), counted) == 3.0 * 15.0 * 0.5 * 0.4 * 0.5, w, "RunSkiersPerDay: ha × 15 × natural × served × open");
            var notCounted = counted;
            notCounted.Counted = false;
            Check(Score.RunSkiersPerDay(Run(Gr, Green, 30000, 0, 0), notCounted) == 0.0 && Score.RunSkiersPerDay(Run(Gr, Green, double.NaN, 0, 0), counted) == 0.0 &&
                  Score.RunSkiersPerDay(Run(Gr, Green, -5, 0, 0), counted) == 0.0, w, "RunSkiersPerDay: not counted / NaN / negative area → 0");
            Check(Score.RunSkiersPerDay(Run(Ni, Blue, 20000, 0, 0), new Score.RunPart { ServedFactor = 1, OpenFactor = 1, Counted = true }) == 50.0, w,
                  "a night run holds no extra skiers (no type factor)");

            Score.RunInput closedNatural = Run(Na, Black, 30000, 1600, 1100);
            closedNatural.OpenFactor = 0;
            var n2 = new List<Score.RunInput>(Medium()) { closedNatural };
            ScoreRow("T1", AreaIn(1), Small(), 1, 30.803316785, 202.5, 0, true, 1250, 1);
            ScoreRow("T2", AreaIn(2), Medium(), 3, 133.913400012, 745, 1, true, 1500, 1);
            ScoreRow("T3", AreaIn(3), Large(), 6, 436.411455391, 2015, 2, true, 1800, 1);
            ScoreRow("N2", AreaIn(2), n2, 3, 156.346190661, 745, 1, true, 1500, 0.856518470);
            var unservedBlacks = Modify(Medium(), (r, i) => { if (i >= 8) r.Served = false; return r; });
            ScoreRow("B1", AreaIn(2, 40), unservedBlacks, 3, 106.994051234, 718, 1, true, 1500, 1);
            ScoreRow("B0", AreaIn(2, 0), unservedBlacks, 3, 89.047818715, 700, 1, true, 1400, 1);
            ScoreRow("B100", AreaIn(2, 100), unservedBlacks, 3, 133.913400012, 745, 1, true, 1500, 1);
            var half = Modify(Medium(), (r, i) => { r.OpenFactor = 0.5; return r; });
            ScoreRow("H50", AreaIn(2), half, 3, 133.913400012, 372.5, 1, true, 1500, 0.5);
            var summer = Modify(Small(), (r, i) => { r.OpenFactor = 0; return r; });
            ScoreRow("W1", AreaIn(1), summer, 1, 30.803316785, 0, 0, false, 0, 0);
            ScoreRow("G0", AreaIn(2, 40, false), Modify(Medium(), (r, i) => { r.OpenFactor = 0; return r; }), 3, 133.913400012, 745, 1, true, 1500, 1);
            var unr = Modify(Small(), (r, i) => { if (i == 2) r.State = Score.BandState.Unrateable; return r; });
            ScoreRow("UNR", AreaIn(1), unr, 1, 21.803316785, 182.5, 0, true, 1250, 1);
            ScoreRow("UNRN", AreaIn(1), Modify(unr, (r, i) => { if (i == 2) r.Kind = Na; return r; }), 1, 20.303316785, 167.5, 0, true, 1250, 1);
            var flat = Small();
            flat[2] = Run(Gr, Flat, 20000, 1400, 900);
            ScoreRow("FLAT", AreaIn(1), flat, 1, 18.803316785, 152.5, 0, true, 1250, 1);
            var split = new List<Score.RunInput>();
            Add(split, 20, Run(Na, DBlack, 1000, 1600, 1000));
            ScoreRow("SPLIT", AreaIn(1), split, 1, 44.355127367, 7.5, 0, true, 1600, 1);
            ScoreRow("BLOB", AreaIn(1), new List<Score.RunInput> { Run(Na, DBlack, 270000, 2420, 1000) }, 1, 49.862593358, 101.25, 0, true, 2420, 1);
            ScoreRow("GREEN +inf", AreaIn(1), new List<Score.RunInput> { Run(Gr, Green, double.PositiveInfinity, 1200, 1000) }, 1, 12, 90000, 0, true, 1200, 1);
            ScoreRow("GREEN 1e9", AreaIn(1), new List<Score.RunInput> { Run(Gr, Green, 1e9, 1200, 1000) }, 1, 12, 90000, 0, true, 1200, 1);
            ScoreRow("FT8", AreaIn(1), Ft8(), 2, 103.304372770, 154.3, 0, true, 1103, 1);
            ScoreRow("FT8 half-open black", AreaIn(1), Modify(Ft8(), (r, i) => { if (i == 0) r.OpenFactor = 0.5; return r; }), 2,
                     103.304372770, 135.475, 0, true, 1103, 0.896602569);
            ScoreRow("Z1", AreaIn(1), new List<Score.RunInput>(), 0, 0, 0, 0, false, 0, 0);

            ScoreProperties();
        }

        private static readonly double[] OpenChoices = { 0, 0, 0.4, 0.45, 0.55, 1, 1, 1 };

        private static Score.RunInput RandomRun(System.Random rng)
        {
            var run = new Score.RunInput();
            int w = rng.Next(7);
            if (w < 5)
            {
                run.State = Score.BandState.Rated;
                run.Band = (byte)rng.Next(5);
            }
            else
            {
                run.State = w == 5 ? Score.BandState.Unrateable : Score.BandState.Pending;
            }
            run.Kind = (byte)rng.Next(4);
            run.AreaM2 = 500.0 + rng.NextDouble() * 300000.0;
            run.Served = rng.NextDouble() < 0.7;
            run.OpenFactor = OpenChoices[rng.Next(OpenChoices.Length)];
            run.BottomM = 800 + rng.Next(800);
            run.TopM = run.BottomM + rng.Next(900);
            return run;
        }

        private static List<Score.RunInput> ReplaceAt(List<Score.RunInput> runs, int i, Score.RunInput run)
        {
            var copy = new List<Score.RunInput>(runs);
            copy[i] = run;
            return copy;
        }

        private static void ScoreProperties()
        {
            const string w = "skiday Q property";
            var rng = new System.Random(20261005);
            var parts = new List<Score.RunPart>();
            string[] names =
            {
                "0 ≤ OpenShare ≤ 1", "RunsValueFull ≥ RunsValue", "gating off with a counted run → OpenShare 1", "RunsCapacity ≥ 0",
                "grow × 1.5: capacity non-decreasing", "serve: capacity non-decreasing", "open further: capacity non-decreasing",
                "open further: OpenShare non-decreasing", "backcountry + 5: capacity non-decreasing", "add a run: capacity non-decreasing",
                "NightRunsCounted ≤ RunsCounted, TopKnown ⇔ a counted run",
            };
            int[] bad = new int[names.Length];
            const int Areas = 5000;
            for (int n = 0; n < Areas; n++)
            {
                var runs = new List<Score.RunInput>();
                int count = rng.Next(12);
                for (int i = 0; i < count; i++)
                    runs.Add(RandomRun(rng));
                Score.AreaInput area = AreaIn(rng.Next(1, 4), 5 * rng.Next(21), rng.NextDouble() < 0.85);
                Score.Result b = Score.Compute(area, runs, Lifts(1), parts);
                double share = Rules.OpenShare(b.RunsValue, b.RunsValueFull);
                if (!(share >= 0.0 && share <= 1.0)) bad[0]++;
                if (!(b.RunsValueFull >= b.RunsValue)) bad[1]++;
                if (!area.Settings.SnowGating && b.RunsCounted > 0 && share != 1.0) bad[2]++;
                if (!(b.RunsCapacity >= 0.0)) bad[3]++;
                if (b.NightRunsCounted > b.RunsCounted || b.TopKnown != (b.RunsCounted > 0)) bad[10]++;
                if (count > 0)
                {
                    int i4 = rng.Next(count);
                    Score.RunInput grow = runs[i4];
                    grow.AreaM2 *= 1.5;
                    if (Score.Compute(area, ReplaceAt(runs, i4, grow), Lifts(1), parts).RunsCapacity < b.RunsCapacity) bad[4]++;
                    int i5 = rng.Next(count);
                    Score.RunInput serve = runs[i5];
                    serve.Served = true;
                    if (Score.Compute(area, ReplaceAt(runs, i5, serve), Lifts(1), parts).RunsCapacity < b.RunsCapacity) bad[5]++;
                    int i6 = rng.Next(count);
                    Score.RunInput open = runs[i6];
                    open.OpenFactor = Math.Max(open.OpenFactor, 0.4 + 0.3 * rng.Next(3));
                    Score.Result o = Score.Compute(area, ReplaceAt(runs, i6, open), Lifts(1), parts);
                    if (o.RunsCapacity < b.RunsCapacity) bad[6]++;
                    if (Rules.OpenShare(o.RunsValue, o.RunsValueFull) < share) bad[7]++;
                }
                Score.AreaInput more = area;
                more.Settings.BackcountryPercent = Math.Min(100, more.Settings.BackcountryPercent + 5);
                if (Score.Compute(more, runs, Lifts(1), parts).RunsCapacity < b.RunsCapacity) bad[8]++;
                var added = new List<Score.RunInput>(runs) { RandomRun(rng) };
                if (Score.Compute(area, added, Lifts(1), parts).RunsCapacity < b.RunsCapacity) bad[9]++;
            }
            for (int i = 0; i < names.Length; i++)
                Check(bad[i] == 0, w, $"{names[i]}: {bad[i]} violation(s) in {Areas} areas");
        }

        // ================================================================== S. worked scenarios (ch1 §8)

        private static void Scenarios()
        {
            const string w = "skiday S scenario";
            Rules.Knobs def = Rules.DefaultKnobs();

            // FT8 hill: capacity 154.3 → 154 (runs), 193 on full powder; car claims fit until ~12:30, no car until ~10:50.
            var parts = new List<Score.RunPart>();
            Score.Result ft8 = Score.Compute(AreaIn(1), Ft8(), Lifts(2), parts);
            double cap = Rules.Capacity(ft8.RunsCapacity, 1, 0, out SkiDayLimit lim);
            double capPowder = Rules.Capacity(ft8.RunsCapacity, 1, 1, out SkiDayLimit _);
            Check(Rules.CapacityPeople(cap) == 154 && lim == SkiDayLimit.Runs && Rules.CapacityPeople(capPowder) == 193, w + " FT8",
                  $"capacity {R(cap)} {lim} → {Rules.CapacityPeople(cap)}, powder {Rules.CapacityPeople(capPowder)}; expected 154 Runs, 193");
            double car = Rules.TravelHours(3360, Rules.StartSpeedCar), noCar = Rules.TravelHours(3360, Rules.StartSpeedNoCar);
            Check(Fits(0.5f, car) && !Fits(0.53125f, car), w + " FT8", "car (1.985 h) fits at 12:00, not at 12:45");
            Check(Fits(0.4479167f, noCar) && !Fits(0.4583333f, noCar), w + " FT8", "no car (3.64 h) fits at 10:45, not at 11:00");
            Check(Near(Rules.Chance(GK.Resident, 75, 3.36, 1, 0, 40, Rules.Confidence(TC.Car, Rules.NewTrack(TC.Car)), def), 0.11375, 1e-9) &&
                  Near(Rules.Chance(GK.Resident, 75, 3.36, 1, 0, 40, Rules.Confidence(TC.NoCarFar, Rules.NewTrack(TC.NoCarFar)), def), 0.011375, 1e-9) &&
                  Near(Rules.Chance(GK.Tourist, 75, 3.36, 1, 1, 40, 1, def), 0.1421875, 1e-9), w + " FT8",
                  "car 0.11375 and no car far 0.011375 through the start tracks; tourist powder 0.1421875");

            // Tiny hill: A 41 (green + blue 1.5 ha, 1,250 / 1,110 m, 1 lodge, 1 lift), 105 skiers (runs).
            var tinyRuns = new List<Score.RunInput> { Run(Gr, Green, 15000, 1250, 1110), Run(Gr, Blue, 15000, 1250, 1110) };
            Score.Result tiny = Score.Compute(AreaIn(1), tinyRuns, Lifts(1), parts);
            double tinyCap = Rules.Capacity(tiny.RunsCapacity, 1, 0, out SkiDayLimit tinyLim);
            Check(tiny.Attraction == 41 && Near(tiny.AOpen, 41.05, 0.01) && Near(tinyCap, 105, 1e-9) && tinyLim == SkiDayLimit.Runs, w + " tiny",
                  $"A {tiny.Attraction} (A_open {R(tiny.AOpen)}), capacity {R(tinyCap)} {tinyLim}; expected 41 (41.05), 105 Runs");
            Check(Near(Rules.Chance(GK.Resident, 41, 0.3, 1, 0, 40, 1, def), 0.1025, 1e-9) && Near(Rules.TravelHours(300, Rules.StartSpeedWalk), 1.3, 1e-9),
                  w + " tiny", "0.3 km walk: p 0.1025, t 1.3 h");
            Check(Near(Rules.Chance(GK.Resident, 41, 2.5, 1, 0, 40, 1, def), 0.076875, 1e-9) && Near(Rules.TravelHours(2500, Rules.StartSpeedCar), 1.477272727, 1e-9),
                  w + " tiny", "2.5 km car: p 0.076875, t 1.48 h");
            double walk = Rules.TravelHours(300, Rules.StartSpeedWalk);
            Check(Fits(0.21875f, walk) && Fits(13f / 24f, walk) && !Fits(0.5520833f, walk), w + " tiny", "the walk fits from 05:15 to 13:00, not at 13:15 (last ~13:12)");

            // Large resort (= T3, A 160, 3 lodges): 1080 (lodges), 1350 on powder; chances at 2 km, 5 km and slope-side tourists.
            Score.Result large = Score.Compute(AreaIn(3), Large(), Lifts(6), parts);
            double lCap = Rules.Capacity(large.RunsCapacity, 3, 0, out SkiDayLimit lLim), lCapP = Rules.Capacity(large.RunsCapacity, 3, 1, out SkiDayLimit _);
            Check(large.Attraction == 160 && lCap == 1080 && lLim == SkiDayLimit.Lodges && lCapP == 1350, w + " large",
                  $"A {large.Attraction}, capacity {R(lCap)} {lLim}, powder {R(lCapP)}; expected 160, 1080 Lodges, 1350");
            Check(Near(Rules.Chance(GK.Resident, 160, 2, 1, 0, 40, 1, def), 0.2, 1e-9) && Near(Rules.Chance(GK.Resident, 160, 2, 1, 1, 40, 1, def), 0.4, 1e-9),
                  w + " large", "2 km: 0.2 (capped), powder 0.4");
            Check(Near(Rules.Chance(GK.Resident, 160, 5, 1, 0, 40, 1, def), 0.133333333, 1e-9) && Near(Rules.Chance(GK.Resident, 160, 5, 1, 1, 40, 1, def), 0.266666667, 1e-9),
                  w + " large", "5 km: 0.1333, powder 0.2667");
            Check(Near(Rules.Chance(GK.Tourist, 160, 0.3, 1, 0, 40, 1, def), 0.3, 1e-9) && Near(Rules.TravelHours(5000, Rules.StartSpeedCar), 2.954545455, 1e-9),
                  w + " large", "slope-side tourists 0.3; 5 km car 2.95 h");
        }

        /// <summary>Does a claim with <paramref name="hours"/> of travel fit at time of day <paramref name="t"/> (day hours,
        /// vanilla's deadline far away)?</summary>
        private static bool Fits(float t, double hours)
        {
            return Rules.TryClosingFrame(Now, t, false, out uint closing) && Rules.ClaimFits(Now, Now + 200000u, closing, hours);
        }

        // ================================================================== T. Round M1 measure rules (m1-spec §4)

        private static void MeasureRules()
        {
            // DistanceBand: < 1 / 3 / 5 / 7 km, NaN and negative → 0.
            const string wd = "skiday T band";
            double[] dm = { 0, 999, 1000, 2999, 3000, 5000, 6999, 7000, 12000, double.NaN, -1 };
            int[] db = { 0, 0, 1, 1, 2, 3, 3, 4, 4, 0, 0 };
            for (int i = 0; i < dm.Length; i++)
                Check(Rules.DistanceBand(dm[i]) == db[i], wd, $"DistanceBand({R(dm[i])}) = {Rules.DistanceBand(dm[i])}, expected {db[i]}");
            Check(Rules.DistanceBands == 5, wd, "DistanceBands 5");

            // Modes (arrival-modes.md §9, with its Verification: Bike for a personal car + bicycle, Other for a bicycle alone).
            const string wm = "skiday T mode";
            Check((byte)Rules.ArrivalMode.Foot == 0 && (byte)Rules.ArrivalMode.Car == 1 && (byte)Rules.ArrivalMode.Transit == 2 &&
                  (byte)Rules.ArrivalMode.Taxi == 3 && (byte)Rules.ArrivalMode.Bike == 4 && Rules.ArrivalModes == 5, wm, "ArrivalMode literals 0..4");
            Check(Rules.LegCar == 1 && Rules.LegTransit == 2 && Rules.LegTaxi == 4 && Rules.LegBike == 8 && Rules.LegOther == 16, wm, "leg bits");
            Check(Rules.KindBus == 1 && Rules.KindTram == 2 && Rules.KindTrain == 4 && Rules.KindSubway == 8 && Rules.KindBoat == 16 &&
                  Rules.KindOther == 32 && Rules.KindBits == 6, wm, "kind bits");
            // Game.Prefabs.TransportType (1.6.2 TransportType.cs:5-19): Bus 0, Train 1, Taxi 2, Tram 3, Ship 4, … Subway 8, … Ferry 11.
            Check(Rules.TransportBus == 0 && Rules.TransportTrain == 1 && Rules.TransportTram == 3 && Rules.TransportShip == 4 &&
                  Rules.TransportSubway == 8 && Rules.TransportFerry == 11, wm, "TransportType literals");
            Check(Rules.MainMode(0) == Rules.ArrivalMode.Foot, wm, "MainMode(0) Foot");
            Check(Rules.MainMode(Rules.LegCar) == Rules.ArrivalMode.Car, wm, "MainMode(Car) Car");
            Check(Rules.MainMode((byte)(Rules.LegCar | Rules.LegTransit)) == Rules.ArrivalMode.Car, wm, "MainMode(Car|Transit) Car");
            Check(Rules.MainMode((byte)(Rules.LegTransit | Rules.LegTaxi)) == Rules.ArrivalMode.Transit, wm, "MainMode(Transit|Taxi) Transit");
            Check(Rules.MainMode((byte)(Rules.LegTaxi | Rules.LegBike)) == Rules.ArrivalMode.Taxi, wm, "MainMode(Taxi|Bike) Taxi");
            Check(Rules.MainMode(Rules.LegBike) == Rules.ArrivalMode.Bike, wm, "MainMode(Bike) Bike");
            Check(Rules.MainMode(Rules.LegOther) == Rules.ArrivalMode.Foot, wm, "MainMode(Other) Foot");
            Check(Rules.MainMode((byte)(Rules.LegOther | Rules.LegTransit)) == Rules.ArrivalMode.Transit, wm, "MainMode(Other|Transit) Transit");
            Check(Rules.MainMode(0xFF) == Rules.ArrivalMode.Car, wm, "MainMode(0xFF) Car");
            Check(Rules.LegOf(true, false, false, false) == Rules.LegCar, wm, "LegOf(car) Car");
            Check(Rules.LegOf(true, true, false, false) == Rules.LegBike, wm, "LegOf(car + bicycle) Bike");
            Check(Rules.LegOf(false, false, true, false) == Rules.LegTaxi, wm, "LegOf(taxi) Taxi");
            Check(Rules.LegOf(false, false, false, true) == Rules.LegTransit, wm, "LegOf(public transport) Transit");
            Check(Rules.LegOf(false, false, false, false) == Rules.LegOther, wm, "LegOf(nothing) Other");
            Check(Rules.LegOf(false, true, false, false) == Rules.LegOther, wm, "LegOf(bicycle alone) Other");
            Check(Rules.LegOf(true, false, true, true) == Rules.LegCar, wm, "LegOf(car + taxi + transit) Car");
            int[] kt = { 0, 3, 1, 8, 4, 11, 7, 2, -1, 99 };
            byte[] kb = { Rules.KindBus, Rules.KindTram, Rules.KindTrain, Rules.KindSubway, Rules.KindBoat, Rules.KindBoat,
                          Rules.KindOther, Rules.KindOther, Rules.KindOther, Rules.KindOther };
            for (int i = 0; i < kt.Length; i++)
                Check(Rules.KindBitOf(kt[i]) == kb[i], wm, $"KindBitOf({kt[i]}) = {Rules.KindBitOf(kt[i])}, expected {kb[i]}");
            // Partition: every legs byte maps to exactly one mode, and that mode is the first present of car, transit, taxi, bike.
            int badPartition = 0;
            for (int legs = 0; legs < 256; legs++)
            {
                Rules.ArrivalMode got = Rules.MainMode((byte)legs);
                Rules.ArrivalMode expect = (legs & 1) != 0 ? Rules.ArrivalMode.Car : (legs & 2) != 0 ? Rules.ArrivalMode.Transit
                    : (legs & 4) != 0 ? Rules.ArrivalMode.Taxi : (legs & 8) != 0 ? Rules.ArrivalMode.Bike : Rules.ArrivalMode.Foot;
                if (got != expect || (byte)got > 4)
                    badPartition++;
            }
            Check(badPartition == 0, wm, $"MainMode partitions the 256 legs bytes: {badPartition} bad");

            // FitVerdict (now 1,000,000; 1 h of travel + MinStayHours 2 = 3 h = 32,768 frames).
            const string wf = "skiday T fit";
            Check(Fv(Now + 100000u, Now + 40000u, 1.0) == Rules.Fit.Fits, wf, "closing +40,000, vanilla +100,000 → Fits");
            Check(Fv(Now + 100000u, Now + 30000u, 1.0) == Rules.Fit.NoTimeBeforeClosing, wf, "closing +30,000 → NoTimeBeforeClosing");
            Check(Fv(Now + 30000u, Now + 40000u, 1.0) == Rules.Fit.NoTimeBeforeOwnDeadline, wf, "vanilla +30,000 → NoTimeBeforeOwnDeadline");
            Check(Fv(Now + 30000u, Now + 30000u, 1.0) == Rules.Fit.NoTimeBeforeClosing, wf, "both +30,000 → NoTimeBeforeClosing (a tie names closing)");
            Check(Fv(Now, Now + 40000u, 1.0) == Rules.Fit.NoTimeBeforeOwnDeadline, wf, "vanilla = now → NoTimeBeforeOwnDeadline");
            Check(Fv(Now - 5u, Now + 40000u, 1.0) == Rules.Fit.NoTimeBeforeOwnDeadline, wf, "vanilla < now → NoTimeBeforeOwnDeadline");
            Check(Fv(Now + 100000u, Now + 40000u, double.NaN) == Rules.Fit.Invalid, wf, "NaN hours → Invalid");
            Check(Fv(Now + 100000u, Now + 40000u, -1.0) == Rules.Fit.Invalid, wf, "negative hours → Invalid");
            Check(Fv(Now + 100000u, Now + 40000u, double.PositiveInfinity) == Rules.Fit.Invalid, wf, "infinite hours → Invalid");
            Check(Fv(Now + 100000u, Now + 32769u, 1.0) == Rules.Fit.Fits && Fv(Now + 100000u, Now + 32767u, 1.0) == Rules.Fit.NoTimeBeforeClosing, wf,
                  "closing + 32,769 → Fits, + 32,767 → NoTimeBeforeClosing (the need is 32,768)");
            var rng = new System.Random(20261005);
            int badFit = 0;
            string firstFit = null;
            for (int n = 0; n < 5000; n++)
            {
                uint now = (uint)rng.Next(0, 2000000000);
                uint vanilla = now + (uint)rng.Next(-20000, 200000);
                uint closing = now + (uint)rng.Next(-20000, 200000);
                double hours = rng.Next(10) == 0 ? (rng.Next(2) == 0 ? double.NaN : -rng.NextDouble()) : rng.NextDouble() * 8.0;
                bool fits = Rules.ClaimFits(now, vanilla, closing, hours);
                if ((Rules.FitVerdict(now, vanilla, closing, hours) == Rules.Fit.Fits) != fits && badFit++ == 0)
                    firstFit = $"now {now}, vanilla {vanilla}, closing {closing}, hours {R(hours)}";
            }
            Check(badFit == 0, wf, $"FitVerdict == Fits ⇔ ClaimFits over 5,000 random rows: {badFit} bad{(firstFit != null ? "; first " + firstFit : "")}");

            // HourDate = RunSnowRules.HourAlignedDate's return for that hour key; MonthOfHour.
            const string wh = "skiday T hour";
            int[] hk = { 0, 1, 143, 287 };
            for (int i = 0; i < hk.Length; i++)
            {
                float hd = Rules.HourDate(hk[i], 12);
                // HourAlignedDate of a date in the middle of that hour returns the hour key and the aligned date (= HourDate).
                float aligned = RunSnowRules.HourAlignedDate((float)((hk[i] + 0.5) / 288.0), 12, out int key);
                Check(hd == (float)(hk[i] / 288.0) && key == hk[i] && aligned == hd, wh,
                      $"HourDate({hk[i]}, 12) = {hd:R} = (float)({hk[i]} / 288) = HourAlignedDate's return for hour key {key}");
            }
            Check(Rules.HourDate(0, 0) == 0f, wh, "HourDate(0, 0) = 0 (days ≥ 1)");
            Check(Rules.MonthOfHour(0, 12) == 0 && Rules.MonthOfHour(23, 12) == 0 && Rules.MonthOfHour(24, 12) == 1 && Rules.MonthOfHour(287, 12) == 11, wh,
                  "MonthOfHour 0 / 23 / 24 / 287 at 12 days = 0 / 0 / 1 / 11");
            Check(Rules.MonthOfHour(47, 24) == 0 && Rules.MonthOfHour(48, 24) == 1, wh, "MonthOfHour 47 / 48 at 24 days = 0 / 1");
            Check(Rules.MonthOfHour(-5, 12) == 0 && Rules.MonthOfHour(100000, 12) == 11, wh, "MonthOfHour clamps to 0..11");

            // Advance(ref f, key, closedShort) (D5): a short closed month reads "since load" (partial).
            const string wa = "skiday T month";
            Rules.MonthFlags f = Rules.NewMonthFlags();
            Check(Rules.Advance(ref f, 100, true) == MonthStep.Started && f.ThisSinceLoad && !f.LastKnown, wa, "Started 100 (a short flag does not change a start)");
            Check(Rules.Advance(ref f, 101, false) == MonthStep.Rolled && f.LastKnown && f.LastSinceLoad, wa, "(101, false) → LastSinceLoad true (since load)");
            Check(Rules.Advance(ref f, 102, true) == MonthStep.Rolled && f.LastKnown && f.LastSinceLoad && !f.ThisSinceLoad, wa, "(102, true) → LastSinceLoad true (short)");
            Check(Rules.Advance(ref f, 103, false) == MonthStep.Rolled && f.LastKnown && !f.LastSinceLoad, wa, "(103, false) → a full last month");
            Check(Rules.Advance(ref f, 103, true) == MonthStep.None && !f.LastSinceLoad, wa, "same key, short → None, flags unchanged");
            Check(Rules.Advance(ref f, 110, true) == MonthStep.Jumped && !f.LastKnown && !f.LastSinceLoad && f.ThisSinceLoad, wa, "(110, true) → Jumped as before");
            // Fixer R7: an independent oracle for the D5 parity the code comments claim — the ski days' month flags and the
            // guest ledger (SkiResortLedger.Advance(key, closedMonthShort), builder B's code) step alike over random keys
            // AND a random short flag (the former check compared Advance(f, key) with Advance(f, key, false), which is
            // the same code path).
            int mism = 0, shortRolls = 0;
            string firstAdv = null;
            for (int s = 0; s < 2000; s++)
            {
                var ledger = new SkiResortLedger();
                Rules.MonthFlags flags = Rules.NewMonthFlags();
                int key = rng.Next(20000, 30000);
                for (int step = 0; step < 50; step++)
                {
                    int pick = rng.Next(3);
                    if (step > 0)
                        key = pick == 0 ? key : (pick == 1 ? key + 1 : rng.Next(20000, 30000));
                    bool closedShort = rng.Next(3) == 0;
                    byte a = (byte)ledger.Advance(key, closedShort);
                    MonthStep b = Rules.Advance(ref flags, key, closedShort);
                    if (closedShort && b == MonthStep.Rolled)
                        shortRolls++;
                    bool same = a == (byte)b && ledger.MonthKey == flags.Key && ledger.ThisMonthSinceLoad == flags.ThisSinceLoad &&
                                ledger.LastMonthKnown == flags.LastKnown && ledger.LastMonthSinceLoad == flags.LastSinceLoad;
                    if (!same && mism++ == 0)
                        firstAdv = $"sequence {s} step {step} key {key} short {closedShort}: ledger {a} vs flags {(byte)b}";
                }
            }
            Check(mism == 0 && shortRolls > 1000, wa,
                  $"Advance(ref f, key, short) = SkiResortLedger.Advance(key, short) over 2,000 × 50 random steps ({shortRolls} short rolls): " +
                  $"{mism} mismatch(es){(firstAdv != null ? "; first " + firstAdv : "")}");
        }

        private static Rules.Fit Fv(uint vanilla, uint closing, double hours)
        {
            return Rules.FitVerdict(Now, vanilla, closing, hours);
        }
    }
}
