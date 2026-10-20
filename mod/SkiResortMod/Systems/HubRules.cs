// HubRules — the hub's PURE rules of Round E1 (docs/research/econ-round/spec/ch5-hub-csharp.md §6.1-6.2;
// design docs/research/econ-round/design/hub-v3-design.md §9): the literal-valued enums of contract 3
// (ResortStatus, RunOpenState, SeasonHint, NextStepKind, HubLiftStatus, HubProblemCode; contract 4 adds
// HubSkiDayState — gate rule R17 of
// mod/SkiResortUI/tools/check-bundle.js compares each name and value with bindings.ts, so every value is
// written out and HubProblemCode is APPEND ONLY), the small rules behind the ResortLive / RunLive / LiftRow
// fields, the area-weighted snow sums, rule 7's guard (FollowCheck, D36) and the problems band
// (HubProblems.Build). Written 2026-10-04 (Round E1, stream S5). Round F7 (2026-10-04, implementer 3;
// docs/research/field-test-7-fixes.md D5): HubProblemCode SignNoRoad = 18 / LodgeNoRoad = 19 appended, with the
// two HubProblemInput fields behind them. Round S1 "Ski days" (2026-10-05, stream D; docs/research/ski-days/
// s1-spec.md §2.9, §5.3, spec/ch4-hub.md §A.2-§A.3, §C): contract 4's HubSkiDayState and SkiDayStateOf; HubProblemCode
// SkiDaysCannotReach = 20 / NoParkingNearLodge = 21 / LodgeNotSlopeside = 22 appended (D28), with their three
// HubProblemInput fields; F1 (the Efficiency row's hysteresis, EfficiencyClearPct) and F1b (a Factor row only from
// FactorRowMinLossPct points, orchestrator ruling R-Q2). Round M1 "measure first" (2026-10-05, Builder B;
// docs/research/ski-days/measure/m1-spec.md M10 D9): LodgeNotSlopeside is not built for an area with no runs.
// Round M2 (2026-10-10, builder B3; docs/research/round-m2/decisions.md R5, understand/thaw-rules.md item 14):
// Season takes its gun word from the live gun gates (gunsLiveKnown, anyGunOn); the 24-hour mean still decides
// natural snow. Enum values unchanged (no contract bump).
//
// CALLERS: SkiResortStatsSystem (every rule of a rebuild; HubProblems.Build per area) and
// SkiResortHubUISystem (LiftNoPair for build[lift].hint, FollowCheck for rule 7, LiftLinePerKm for the
// Build strip). Never the UI: the UI receives the results as numbers and enum values (D10: the hub reads
// SkiResortScore's Result, it never recomputes the score — AttractionWithLodges is the one "what if"
// and uses the model's own LodgeBaseOf / RoundToEven / MaxAttraction).
//
// RULES (ch5 §6.2, ski-days ch4 §A.3 / §C; tests/ResortBatch HubRulesTests sections A-H are the literal oracles):
//   Status            runs <= 0 NoRuns; runsOpen >= runs Open; open + partly <= 0 Closed; else Partly
//                     (the status line prints runsOpen of runs: word and count use the same sets)
//   OpenStateOf       not a member None; !(f > 0) Closed (NaN too); f >= 1 Open; else Partly
//   Season            gating off GatingOff; no run / unknown top temperature None; natural = T <= freezing
//                     (T = the 24-hour day mean at the top); gun = a gun run && (Round M2 R5) the live gun
//                     gates when known (anyGunOn: a bound snowmaking or night run with gun-gated texels at the
//                     last mask pass), else (before the first mask pass, or the injection off) T <= freezing +
//                     margin (both inclusive); with an open run: natural SnowSeason, else gun SnowGunsOnly, else
//                     Thawing; none open: natural or gun WaitingForSnow, else TooWarm
//   NextStep          lodge, then a complete lift, then a run, else Done
//   LiftStatusOf      stats state 0 powered Running / NoPower; 1 powered ChairsStopped / NoPower;
//                     2 SectionMissing; anything else Unfinished. T132 (2026-10-20): the closed overload — a complete, powered lift
//                     (state 0 or 1) held by the no-open-run rule (LiftClosedRule, the tram exempt) reads Closed, not Running
//   RideTimeS         loop / 2 / ChairSpeedMps (a chair rides half the loop); −1 without a loop
//   OpenSharePct      round(100 × value / full) half away from zero, 0..100; −1 when full is not > 0
//   FactorEffectPct   the game's panel number (Game.UI.InGame/EfficiencySection.cs:99-127):
//                     max(−99, round(100f × e) − 100) with the multiply in FLOAT and Math.Round(double)
//                     half to even (= math.round); −100 when e is not > 0
//   CountedFraction   the share of the calendar month the ledger's "this month" covers (since load: also
//                     capped by the frames simulated since the load); 0 for a non-finite date
//   TicketIncomeMonth last month when it was counted in full; else this month projected from at least
//                     MinCountedFraction of a month; else −1 (unknown)
//   NetMonth          (B1, 2026-10-19; was max(0, tickets + lodge spend) − cost rates) THE one Net of the hub, over the
//                     eight terms of HubMonthMoney: tickets + lodge spend + lessons + passes sold − (lift charge + run
//                     upkeep + buildings + wages), each term < 0 (unknown) read as 0; estimated when a term is unknown.
//                     MonthMoneyOf maps a ledger Month to the terms with the history element's rounding; MonthShare is
//                     the share of the month simulated that scales the two rate snapshots (header B1 below)
//   AttractionWithLodges  the model's A with `lodges` active lodges at the same efficiency: swap the lodge
//                     base, clamp to 0..MaxAttraction BEFORE rounding (ch1 §5 step 7), RoundToEven
//   LiftNoPair        false when one row of the shown area has both stations live and a line to draw or
//                     finish (state 2, 3 or 4), or (E1 fixer F9) a lone base and a lone top sit on two rows
//                     of state 3 or 4 (the census never puts both on one such row); else true (the empty list too)
//   Percent, TopTempC, LodgePart, Terrain, RunValueFull: the expressions named on each method
//   FollowCheck       (order matters) hub closed; a rule switched in this pass; within
//                     FollowQuietAfterUserS of the player's own switch; within FollowMinIntervalS of the
//                     last follow; else Follow. −∞ = never; a NaN comparison is false, so it allows
//   SkiDayStateOf     (Round S1, ch4 §A.3; first match wins) off Off; no view Unknown; no score or the
//                     first-write hold Unknown; sign not running SignOff; runs counted <= 0 NoRun; target
//                     lodges <= 0 NoLodge; outside the claim hours Night; cap < 0 or now < 0 Unknown (before the
//                     first pass); now >= cap Full (a capacity of 0 is real: the simulation sends nobody,
//                     SkiDayRules.HasRoom(x, 0) is false); else Room
//   HubProblems.Build (ch5 §6.2): Unlinked rows only for the Not linked entry; else lodges, road edges (Round F7
//                     D5: SignNoRoad severity 3; LodgeNoRoad 3 when every lodge, else 1, count = lodges with no
//                     road), efficiency (Round S1 F1: at <= EfficiencyProblemPct, and kept while the band showed it
//                     on the area's previous build and the percent is still below EfficiencyClearPct; "more" =
//                     the factors listed as Factor rows − 1, or every factor − 1 when none is listed: fixer, HU-1),
//                     lifts (E1 fixer F9: lone stations of state 4 paired across rows — one "draw the lift
//                     line" row per base + top pair), no lift, no runs, unserved, unrateable / rating / flat, the
//                     ski-day rows (Round S1 D28: SkiDaysCannotReach severity 2; NoParkingNearLodge and
//                     LodgeNotSlopeside severity 1, count = min(n, Lodges), none when 0; Round M1 D9:
//                     LodgeNotSlopeside only when the area has runs, Runs > 0) — then a STABLE
//                     insertion sort by (severity descending, code ascending, insertion order), cut to
//                     MaxBandRows, then one Factor row per efficiency factor that costs at least
//                     FactorRowMinLossPct points (Round S1 F1b; severity 0, never in the band)
//
// CATALOG PHASE B (2026-10-11, docs/research/art/assets-2026-10/code-round-2/round-2-contract.md §6.1 with
// round-2-contract-skeptic.md MUST 1, lift group):
//   RideTimeS(loop, speed) — loop / 2 / speed (−1 without a finite loop > 0 or a speed > 0); RideTimeS(loop) stays
//                     = RideTimeS(loop, ChairSpeedMps) (CG-2);
//   HubProblemCode    APPEND MixedLiftTypes = 23, SummitLodgeNotLiftServed = 24, SchoolNoGreenRun = 25;
//   HubProblems.Build a lift row with Mixed writes MixedLiftTypes (severity 2, its line item by type) instead of
//                     LiftUnfinished; every other lift row's build item comes from the row's type (LineItemOf /
//                     StationItemOf; fixed-grip = today's "lift" / "baseStation" / "topStation"); NoLift keeps
//                     "baseStation"; SummitLodgesNotServed > 0 → row 24 (severity 1, count); SchoolNoGreenRun → row 25
//                     (severity 1);
// T100 (2026-10-19, docs/research/ui/hub-school-launch-2026-10-19.md; contract 11 unchanged: codes are not in the lock):
//   HubProblemCode    APPEND SchoolsNotLinked = 26 (Not linked entry: FreeSchools > 0 → severity 1, count);
//   HubProblems.Build LodgeNoRoad counts min(LodgesNoRoad, Lodges) + SchoolsNoRoad (top-level schools with no road edge);
//                     severity 3 only when every LODGE has no road, else 1 (a school alone: 1);
//   HubLiftHintRow.Type — the hub calls LiftNoPair per lift type over the rows of that type (pure rule unchanged);
//   LodgePart         = r.LodgeApplied (seam S4: the ladder's season value; for A-frames = LodgeBase × LodgeShare);
//   AttractionWithLodges(r, n) (MUST 1) = r.AOpen − r.LodgeApplied + LodgeBaseOf(n) × (summer ? SummerLodgeShare : 1):
//                     n A-frames in place of the area's applied lodge base — no base × share reconstruction, so a
//                     SUMMER area with no lodge keeps today's "if fixed" number (its LodgeApplied is 0);
//   AttractionWithLodgeApplied(r, applied) = r.AOpen − r.LodgeApplied + applied: the stats system's IfLodgesOn, with
//                     the ladder's applied value of the area's lodges all switched on (SkiResortScore.LodgeBase).
//   Both clamp to 0..MaxAttraction before RoundToEven, at the Result's efficiency, as before.
//
// CATALOG PHASE C (2026-10-11, docs/research/art/assets-2026-10/code-round-3/round-3-contract.md §6.1 / C5 with
// round-3-contract-skeptic.md MUST 4, rules group): the tram (a Shuttle lift type) gets its two hub numbers from the
// SAME pure profile the mover uses (ShuttleProfile.cs), so the hub shows what the player sees:
//   ShuttleRideTimeS(path, v, a, dwell) — ShuttleProfile.TripTimeS (dock to dock, the trapezoid's ramps included, the
//                     dwell excluded: C5, "time riding" as for every lift type); −1 unless ShuttleProfile.Valid(path, v,
//                     a, dwell). The dwell is a PARAMETER (MUST 4) and ShuttleRideIncludesDwell (false) is the one switch:
//                     the brief said "ride time L/v + dwell"; C5 departs from it and the choice is Richard's (open
//                     decision, code-round-3/build-rules.md) — flipping it changes this one constant, HubRulesTests J's
//                     ride oracles and the LIFT_RIDE_TIP wording, nothing else;
//   ShuttleCapacityPerHour(cabins, seats, path, v, a, dwell) — ShuttleProfile.CapacityPerHour: cabins × seats × 3600 /
//                     (2 × (trip + dwell)) (the cabin is unavailable while it dwells); −1 when invalid, cabins ≤ 0 or
//                     seats ≤ 0. The brief's "pax × 3600 / (L/v + dwell)" is the a → ∞ limit with both cabins (HJ-6).
//   Both widen their float inputs to double once and narrow the result once (the float oracles HJ-1..HJ-5 are bit-exact).
//
// PHASE D (2026-10-11, docs/research/art/assets-2026-10/code-round-4/round-4-contract.md §2.6, D9, seam S3; rules group):
// Richard's re-ruling, DECISIONS 171 — an unpowered lift of every type carries no guests, so it shows no capacity (Richard's
// O2 tram gate generalized to every type):
//   LiftCapacityShown(capacity, carries) — capacity when the lift carries (LiftServedRule.Carries: complete, powered at its
//                     base station, not closed), else −1 (the UI's "–", LiftTable.tsx). A carrying row returns its argument
//                     bit for bit (ResortBatch HK-1), so a powered lift's number and the area sum are unchanged. Status
//                     (LiftStatusOf: "No power"), the chairs count and the ride time are not touched by it.
//
// B1 "ONE NET" (2026-10-19, Richard; docs/research/ui/hub-one-net-b1-2026-10-19.md, review hub-design-review-2026-10-18.md F1 / F2):
// every Net the hub shows (the Finances tile, the Finances view, History's months and its month in progress) is NetMonth over
// the SAME eight terms (HubMonthMoney), so they cannot disagree:
//   income  TicketIncome   ledger TicketIncome            BOOKED this month (₵ paid at the gate)
//           LodgeIncome    ledger LodgeSpendIncome        BOOKED this month
//           LessonIncome   ledger LessonIncome            BOOKED this month
//           PassIncome     ledger PassIncome              BOOKED this month = passes SOLD this month, pre-season sales included
//                                                         (they land in the month they were sold: "a big September is honest")
//   costs   LiftCost       ledger LiftCharged, rounded    BOOKED this month (SkiLiftChargeSystem's accrual)
//           RunCost        ledger RunUpkeep               BOOKED this month, per area (T78 P3, SkiRunUpkeepSystem)
//           BuildingsCost  the game's upkeep formula      RATE SNAPSHOT: ₵/month at the current service budget × MonthShare for
//           WagesCost      for the sign + its lodges      the month in progress; the rate at the roll for a closed month
//                                                         (the game bills these through the city budget; nothing books them per area)
// Net = Σ income − Σ costs, every term < 0 (unknown) read as 0, so the rows the UI shows (max(0, term) each) add up to the Net
// exactly (ResortBatch HubRulesTests A "B1" and HistoryTests H-10). Estimated (the "≈") = some term is unknown: the ledger not
// counting yet, or the building bill unreadable. A month counted only since a load is PARTIAL, not estimated: ResortLive's
// thisMonthSinceLoad carries that, as for the guests. Rounding = the history element's (SkiResortHistoryMonth.FromLedger:
// ClampInt of the longs, LiftCharged half away from zero), so a closed month's element and MonthMoneyOf(its ledger Month) are
// the same eight integers.
//
// PURE FILE: `using System;` and `using System.Collections.Generic;` only, plus SkiResortScore (pure,
// ch1) and SkiCatalog consts (Catalog Phase A, 2026-10-11: ChairSpeedMps and LiftLinePerKm alias
// SkiCatalog.FixedGrip — compile-time literals; SkiCatalog.cs is linked wherever this file is, tests/ResortBatch),
// and (Catalog Phase C) the pure ShuttleProfile.cs, linked into tests/ResortBatch next to this file; (B1) the pure
// SkiResortLedger.cs for MonthMoneyOf's Month. No Entities, no Game, no Unity.Mathematics. net48 traps: no Math.Clamp, MathF, double.IsFinite,
// Span<T>; private Clamp and IsFinite below. No allocation once the caller's lists have grown.

using System;
using System.Collections.Generic;

namespace SkiResortMod.Systems
{
    /// <summary>bindings.ts RESORT_STATUS (gate R17: literal values).</summary>
    public enum ResortStatus : byte { NoRuns = 0, Closed = 1, Partly = 2, Open = 3 }

    /// <summary>bindings.ts OPEN_STATE (gate R17).</summary>
    /// <summary>T136 (field 2026-10-20 note 24): bindings.ts RUN_GUN_WHY (gate R17), RunLive.gunWhy: on a snowmaking or night run,
    /// whether its snow guns make snow now and why not. None = not a gun run, not bound, or snow gating off.</summary>
    public enum RunGunWhy : byte { None = 0, Making = 1, DepthReached = 2, TooWarm = 3, OutOfSeason = 4, Waiting = 5 }

    public enum RunOpenState : sbyte { None = -1, Closed = 0, Partly = 1, Open = 2, Unsuitable = 3 }   // T54 probe 1: Unsuitable = 3, a cat track rated blue or steeper

    /// <summary>bindings.ts SEASON_HINT (gate R17).</summary>
    public enum SeasonHint : byte { None = 0, SnowSeason = 1, SnowGunsOnly = 2, Thawing = 3, WaitingForSnow = 4, TooWarm = 5, GatingOff = 6 }

    /// <summary>bindings.ts NEXT_STEP (gate R17).</summary>
    public enum NextStepKind : byte { Done = 0, Lodge = 1, Lift = 2, Run = 3 }

    /// <summary>bindings.ts LIFT_STATUS (gate R17).</summary>
    public enum HubLiftStatus : byte { Running = 0, NoPower = 1, ChairsStopped = 2, SectionMissing = 3, Unfinished = 4,
        // T132 (field 2026-10-20 note 20: every lift read "Running" in October while only the tram ran): a complete, powered lift
        // held still because its ski area has no open run (LiftClosedRule.ClosedForLift; a tram is never held). Appended.
        Closed = 5 }

    /// <summary>bindings.ts PROBLEM (gate R17). APPEND ONLY, never renumber.</summary>
    public enum HubProblemCode : byte
    {
        Unlinked = 0, NoLodge = 1, LodgesAllOff = 2, LodgesSomeOff = 3, Efficiency = 4, LiftNoPower = 5,
        LiftSectionMissing = 6, LiftUnfinished = 7, NoLift = 8, NoRuns = 9, StationsOnly = 10,
        LiftChairsStopped = 11, RunsUnserved = 12, RunsUnrateable = 13, RunsRating = 14, FreeLodges = 15, Factor = 16,
        RunsFlat = 17,   // second pass (e1-spec §2.5 H1): appended after Factor
        SignNoRoad = 18, LodgeNoRoad = 19,   // Round F7 D5 (field-test-7-fixes.md): appended (gate R17)
        SkiDaysCannotReach = 20, NoParkingNearLodge = 21, LodgeNotSlopeside = 22,   // Round S1 D28 (ski-days ch4 §C.1): appended
        MixedLiftTypes = 23, SummitLodgeNotLiftServed = 24, SchoolNoGreenRun = 25,   // Catalog Phase B (round-2-contract §6.1): appended
        SchoolsNotLinked = 26,   // T100 (hub-school-launch-2026-10-19.md): top-level ski schools on no sign, Not linked entry only
    }

    /// <summary>bindings.ts SKI_DAY_STATE (gate R17): ResortLive.skiDaysState, the Guests view's state line (Round
    /// S1, contract 4, ski-days spec ch4 §A.2). The hub's display state; SkiDaySystem keeps its own.</summary>
    public enum HubSkiDayState : byte { Unknown = 0, Off = 1, SignOff = 2, NoRun = 3, NoLodge = 4, Night = 5, Room = 6, Full = 7 }

    /// <summary>Rule 7's verdict (D36; C# only, not in the contract).</summary>
    public enum FollowVerdict : byte { Follow = 0, HubClosed = 1, RuleSwitched = 2, UserJustSelected = 3, TooSoon = 4 }

    /// <summary>One lift row of the shown area for LiftNoPair (State = the stats LIFT_STATE). Catalog Phase B: Type = the
    /// row's SkiCatalog.Lifts index — the hub calls LiftNoPair once per lift type with that type's rows.</summary>
    public struct HubLiftHintRow { public int State; public bool HasBase, HasTop; public byte Type; }

    /// <summary>One efficiency factor below 100 % (worst first): the EfficiencyFactor enum NAME and the
    /// game's effect number (FactorEffectPct).</summary>
    public struct HubProblemFactor { public string Name; public int EffectPct; }

    /// <summary>One lift row of the area in `lifts` order (State = the stats LIFT_STATE). CustomName (E1
    /// fixer pass 2) = the row's name is a station's custom name (LiftStats.CustomName), not "Lift N". Catalog Phase B
    /// (round-2-contract §6.1): Type = the row's SkiCatalog.Lifts index (its build items; 0 = fixed-grip, the struct
    /// default, so every pre-Phase-B constructor keeps today's items), Mixed = its component spans two lift types.</summary>
    public struct HubProblemLift { public int State; public bool Powered, HasBase, HasTop, CustomName; public byte Type; public bool Mixed; }

    /// <summary>The inputs of HubProblems.Build for ONE area (ch5 §5.6).</summary>
    public struct HubProblemInput
    {
        public bool Unlinked;
        public int Lodges, LodgesActive, FreeLodges;
        public int EfficiencyPct;                 // −1 = no result
        public List<HubProblemFactor> Factors;    // < 100 %, worst first; may be null = none
        public List<HubProblemLift> Lifts;        // the area's lift rows, `lifts` order; may be null = none
        public int LiftRows, Runs, RunsUnserved, RunsUnrateable, RunsRating, RunsFlat, BackcountryPct;   // RunsFlat: second pass
        public int AttractionFull;                // −1 = unknown
        public int IfOneLodge, IfLodgesOn;        // AttractionWithLodges; −1 = unknown
        // Round F7 D5: Building.m_RoadEdge is Entity.Null on the sign / on this many of the area's lodges.
        public bool SignNoRoad;
        public int LodgesNoRoad;
        // Round S1 D28 (ch4 §C): set by the stats system only while ski days are on (not Off) and the area has a view.
        public bool SkiDaysCannotReach;          // D15 (ch2: car confidence < 0.5 after ≥ 20 car claims)
        public int LodgesNoParking;              // D26 (ch3 census): ski-day lodges with 0 car spaces within 300 m
        public int LodgesNotSlopeside;           // D10: active lodges more than 800 m from the area's runs and stations
        // Round S1 F1: the band showed the Efficiency row on this area's previous build (hysteresis state, ch4 §C.4).
        public bool EfficiencyRowWasOn;
        // Catalog Phase B (round-2-contract §6.1, §6.4): active lodges whose row counts more when summit-served
        // (MuSummit > Mu) and that no complete, powered lift top reaches within 300 m; the school is active and runs
        // count but none of them is a counted green run (its +12 and its demand share need one, D13).
        public int SummitLodgesNotServed;
        public bool SchoolNoGreenRun;
        // T100 (school-top-level-t100.md section 4 items 4-5): the area's top-level ski schools with no road edge (they join
        // the LodgeNoRoad row's count, never its "every lodge" severity), and on the Not linked entry the top-level schools
        // linked to no sign (row SchoolsNotLinked).
        public int SchoolsNoRoad;
        public int FreeSchools;
    }

    /// <summary>One problems row (band or Factor) before the stats system adds the area's entities.</summary>
    public struct HubProblem
    {
        public HubProblemCode Code; public byte Severity; public int LiftIndex;   // −1 when not a lift code
        public int Count, Pct, More, Detail, IfFixed;                             // defaults −1, −1, 0, 0, −1
        public string Factor, BuildItem;                                          // "" when none
    }

    /// <summary>Area-weighted snow sums (ch5 §5.2). Add ignores a run whose area is not &gt; 0 and finite.</summary>
    public struct HubSnowSums
    {
        public double Area, ShareArea, DepthArea, DepthWeighted;
        public int Runs;            // runs with a finite share
        public bool AnyEstimated;

        public void Add(double areaM2, double share, double meanDepthM, bool estimated)
        {
            if (estimated)
                AnyEstimated = true;
            if (!(areaM2 > 0.0) || !HubRules.IsFiniteD(areaM2))
                return;
            if (HubRules.IsFiniteD(share))
            {
                Runs++;
                Area += areaM2;
                ShareArea += areaM2 * HubRules.ClampD(share, 0.0, 1.0);
            }
            if (HubRules.IsFiniteD(meanDepthM) && meanDepthM >= 0.0)
            {
                DepthArea += areaM2;
                DepthWeighted += areaM2 * meanDepthM;
            }
        }

        /// <summary>100 × ShareArea / Area; −1 when Runs == 0 or Area ≤ 0.</summary>
        public float SnowPct()
        {
            if (Runs == 0 || !(Area > 0.0))
                return -1f;
            return (float)(100.0 * ShareArea / Area);
        }

        /// <summary>100 × DepthWeighted / DepthArea (cm); −1 when DepthArea ≤ 0.</summary>
        public float DepthCm()
        {
            if (!(DepthArea > 0.0))
                return -1f;
            return (float)(100.0 * DepthWeighted / DepthArea);
        }
    }

    /// <summary>B1 (header "ONE NET"): the eight terms of one month's Net, whole ₵, each −1 when unknown. Booked: the six income /
    /// lift / run terms; rate snapshots: BuildingsCost and WagesCost.</summary>
    public struct HubMonthMoney
    {
        public int TicketIncome, LodgeIncome, LessonIncome, PassIncome;
        public int LiftCost, RunCost, BuildingsCost, WagesCost;

        /// <summary>Every term unknown (−1).</summary>
        public static HubMonthMoney Unknown => new HubMonthMoney
        {
            TicketIncome = -1, LodgeIncome = -1, LessonIncome = -1, PassIncome = -1,
            LiftCost = -1, RunCost = -1, BuildingsCost = -1, WagesCost = -1,
        };

        /// <summary>Σ max(0, income term).</summary>
        public long Income => (long)Math.Max(0, TicketIncome) + Math.Max(0, LodgeIncome) + Math.Max(0, LessonIncome) + Math.Max(0, PassIncome);

        /// <summary>Σ max(0, cost term).</summary>
        public long Costs => (long)Math.Max(0, LiftCost) + Math.Max(0, RunCost) + Math.Max(0, BuildingsCost) + Math.Max(0, WagesCost);
    }

    /// <summary>The hub's pure rules (ch5 §6.1-6.2). Header RULES.</summary>
    public static class HubRules
    {
        // Catalog Phase A (ext row 15; was HubRules.cs:208 = 2.5f, the second copy of the chair speed, compared with
        // ChairPinSystem's at the stats arm, ch5 §5.8): an alias of SkiCatalog.FixedGrip.SpeedMps, as ChairPinSystem's is
        // — one source, so the runtime compare is gone (tests/ResortBatch CatalogGuardTests CG-1, CG-2).
        public const float ChairSpeedMps = SkiCatalog.FixedGrip.SpeedMps;
        public const float MinCountedFraction = 0.25f;  // never project tickets from less than a quarter month
        public const int EfficiencyProblemPct = 90;     // the band shows efficiency at or below this
        public const int EfficiencyClearPct = 95;       // Round S1 F1: a shown Efficiency row stays until this (FT8: 98 flaps)
        public const int FactorRowMinLossPct = 5;       // Round S1 F1b (ruling R-Q2): a Factor row only from a 5-point loss
        public const int MaxBandRows = 8;
        // Catalog Phase A (ext row 16; was HubRules.cs:214 = 7500): D24's documented hand copy (60 per 8 m piece × 125
        // pieces per km, ground level) lives in SkiCatalog.FixedGrip.LinePerKm; this aliases it (CG-1).
        public const int LiftLinePerKm = SkiCatalog.FixedGrip.LinePerKm;
        public const int TicksPerDay = 262144;          // TimeSystem.kTicksPerDay (TimeSystem.cs:18)
        public const float FollowMinIntervalS = 2f;     // D36: at most one follow per 2 s of real time
        public const float FollowQuietAfterUserS = 2f;  // D36: no follow within 2 s of the player's own switch

        public static ResortStatus Status(int runs, int runsOpen, int runsPartly)
        {
            if (runs <= 0)
                return ResortStatus.NoRuns;
            if (runsOpen >= runs)
                return ResortStatus.Open;
            if (runsOpen + runsPartly <= 0)
                return ResortStatus.Closed;
            return ResortStatus.Partly;
        }

        public static RunOpenState OpenStateOf(double openFactor, bool member)
        {
            if (!member)
                return RunOpenState.None;
            if (!(openFactor > 0.0))
                return RunOpenState.Closed;
            return openFactor >= 1.0 ? RunOpenState.Open : RunOpenState.Partly;
        }

        /// <summary>T54 probe 1 (cat-track-run.md §2, §4): the run-state word of a CAT TRACK row. A cat track is never a "member"
        /// of the resort's numbers (RunKindRules.CountsTowardResort), yet the list still says whether it is open: Unsuitable
        /// when rated blue or steeper (SkiRunRatingRules.CatTrackUnsuitable on the ROW band), else the open factor read as a
        /// member would (the day-mean estimate; the track is never injected).</summary>
        public static RunOpenState CatTrackOpenState(double openFactor, sbyte rowBand)
        {
            return SkiRunRatingRules.CatTrackUnsuitable(rowBand) ? RunOpenState.Unsuitable : OpenStateOf(openFactor, true);
        }

        /// <summary>Round M2 R5: the gun word reads the live gun gates (gunsLiveKnown = the injection has measured a
        /// mask this load and is active, or it is not active at all and injects nothing (fixer F6; the caller passes
        /// anyGunOn false then); anyGunOn = a bound snowmaking or night run had gun-gated texels at the last mask pass);
        /// without them (the load to the first mask pass) it falls back to the old mean-based test. The 24-hour mean
        /// decides natural snow.</summary>
        public static SeasonHint Season(bool gating, int runs, int runsOpenAny, bool anyGunRun, bool gunsLiveKnown,
                                        bool anyGunOn, bool topTempKnown, float topTempC, float freezingC, float marginC)
        {
            if (!gating)
                return SeasonHint.GatingOff;
            if (runs <= 0 || !topTempKnown || float.IsNaN(topTempC))
                return SeasonHint.None;
            bool natural = topTempC <= freezingC;                                   // the 24-hour mean at the top (unchanged)
            bool gun = anyGunRun && (gunsLiveKnown ? anyGunOn                       // R5: the live gun gates
                                                   : topTempC <= freezingC + marginC);   // before the first mask pass
            if (runsOpenAny > 0)
                return natural ? SeasonHint.SnowSeason : gun ? SeasonHint.SnowGunsOnly : SeasonHint.Thawing;
            return natural || gun ? SeasonHint.WaitingForSnow : SeasonHint.TooWarm;
        }

        public static NextStepKind NextStep(int lodges, int liftsComplete, int runs)
        {
            if (lodges <= 0)
                return NextStepKind.Lodge;
            if (liftsComplete <= 0)
                return NextStepKind.Lift;
            if (runs <= 0)
                return NextStepKind.Run;
            return NextStepKind.Done;
        }

        /// <summary>T136: the run tooltip's snow-gun line. <paramref name="gunRun"/> = a bound snowmaking or night run;
        /// <paramref name="gating"/> = the snow gating setting is on; <paramref name="inSeason"/> = SnowGunPlumeRules.InGunSeason
        /// (T135); <paramref name="stateKnown"/> / <paramref name="stateRunning"/> = the run's gun hysteresis state (Unknown = not
        /// known; Running vs Stopped); <paramref name="runningNow"/> = SnowGunPlumeRules.GunsRunning for the last pass (Running and
        /// a cold texel). Order: not a gun run, out of season, no measurement yet, making snow, too warm (Running but no texel cold
        /// enough), else stopped at the 3 ft depth (T133: built, or natural snow that deep).</summary>
        public static RunGunWhy GunWhyOf(bool gunRun, bool gating, bool inSeason, bool stateKnown, bool stateRunning, bool runningNow)
        {
            if (!gunRun || !gating)
                return RunGunWhy.None;
            if (!inSeason)
                return RunGunWhy.OutOfSeason;
            if (!stateKnown)
                return RunGunWhy.Waiting;
            if (runningNow)
                return RunGunWhy.Making;
            return stateRunning ? RunGunWhy.TooWarm : RunGunWhy.DepthReached;
        }

        /// <summary>T136: a run's coverage-weighted mean snow depth in whole centimeters; -1 when not measured (NaN, an
        /// estimate) or not finite.</summary>
        public static int DepthCm(float meanDepthM)
        {
            if (float.IsNaN(meanDepthM) || float.IsInfinity(meanDepthM) || meanDepthM < 0f)
                return -1;
            return (int)Math.Round(meanDepthM * 100.0, MidpointRounding.AwayFromZero);
        }

        public static HubLiftStatus LiftStatusOf(int statsState, bool powered)
        {
            return LiftStatusOf(statsState, powered, false);
        }

        /// <summary>T132: <paramref name="closed"/> = the lift's held-closed flag (SkiLiftChargeSystem LiftCharge.Closed: its area has
        /// no open run, one game hour of hysteresis; never a tram). A complete lift (state 0 or 1) that is powered and closed reads
        /// Closed whether or not its chairs are on the loop (held chairs stand on it: state 0 with riding &gt; 0 used to read
        /// Running). No power wins over Closed (the fix the player can make); SectionMissing / Unfinished are not complete.</summary>
        public static HubLiftStatus LiftStatusOf(int statsState, bool powered, bool closed)
        {
            if ((statsState == 0 || statsState == 1) && powered && closed)
                return HubLiftStatus.Closed;
            switch (statsState)
            {
                case 0:
                    return powered ? HubLiftStatus.Running : HubLiftStatus.NoPower;
                case 1:
                    return powered ? HubLiftStatus.ChairsStopped : HubLiftStatus.NoPower;
                case 2:
                    return HubLiftStatus.SectionMissing;
                default:
                    return HubLiftStatus.Unfinished;
            }
        }

        /// <summary>Seconds a chair takes from base to top: half the loop at ChairSpeedMps; −1 without a
        /// finite loop &gt; 0. Catalog Phase B: = RideTimeS(loopM, ChairSpeedMps) (CG-2).</summary>
        public static float RideTimeS(float loopM)
        {
            return RideTimeS(loopM, ChairSpeedMps);
        }

        /// <summary>Catalog Phase B (round-2-contract §6.1): seconds a carrier takes from base to top on a loop of
        /// <paramref name="loopM"/> at <paramref name="speedMps"/> (LiftTypeDef.SpeedMps): half the loop / speed; −1
        /// without a finite loop &gt; 0 or a finite speed &gt; 0.</summary>
        public static float RideTimeS(float loopM, float speedMps)
        {
            if (!IsFiniteF(loopM) || !(loopM > 0f) || !IsFiniteF(speedMps) || !(speedMps > 0f))
                return -1f;
            return loopM / 2f / speedMps;
        }

        /// <summary>Catalog Phase C (round-3-contract C5 with round-3-contract-skeptic MUST 4): false = a tram row's ride
        /// time is the trip alone (C5, as built); true = trip + dwell (the brief's "L/v + dwell"). OPEN DECISION for
        /// Richard (code-round-3/build-rules.md): this constant is the whole flip (plus HubRulesTests J's ride oracles and
        /// the LIFT_RIDE_TIP wording).</summary>
        public const bool ShuttleRideIncludesDwell = false;

        /// <summary>Catalog Phase C (round-3-contract §6.1, C5): seconds a tram cabin rides dock to dock on a path of
        /// <paramref name="pathM"/> (ShuttleView.PathM) at line speed <paramref name="speedMps"/> with the trapezoid's
        /// <paramref name="accelMps2"/> ramps (ShuttleProfile.TripTimeS: 1 km at 10 m/s, 0.8 m/s² = 112.5 s); the dwell is
        /// added only when ShuttleRideIncludesDwell. −1 unless ShuttleProfile.Valid(path, speed, accel, dwell).</summary>
        public static float ShuttleRideTimeS(float pathM, float speedMps, float accelMps2, float dwellS)
        {
            if (!ShuttleProfile.Valid(pathM, speedMps, accelMps2, dwellS))
                return -1f;
            double tripS = ShuttleProfile.TripTimeS(pathM, speedMps, accelMps2);
            return (float)(ShuttleRideIncludesDwell ? tripS + dwellS : tripS);
        }

        /// <summary>Catalog Phase C (round-3-contract §6.1, C5): riders an hour of a tram with <paramref name="cabins"/>
        /// cabins on its path (ShuttleView.CabinsOnPath) of <paramref name="seats"/> seats (LiftTypeDef.SeatsPerCarrier):
        /// ShuttleProfile.CapacityPerHour = cabins × seats × 3600 / (2 × (trip + dwell)) (2 cabins × 100 at 1 km: 2,087).
        /// −1 when the profile is invalid, cabins ≤ 0 or seats ≤ 0.</summary>
        public static float ShuttleCapacityPerHour(int cabins, int seats, float pathM, float speedMps, float accelMps2, float dwellS)
        {
            double capacity = ShuttleProfile.CapacityPerHour(cabins, seats, pathM, speedMps, accelMps2, dwellS);
            return capacity < 0.0 ? -1f : (float)capacity;
        }

        /// <summary>Phase D (round-4-contract §2.6, D9; seam S3): the capacity the hub shows for a lift row —
        /// <paramref name="capacity"/> when the lift carries (LiftServedRule.Carries: complete, powered at its base station,
        /// not closed), else −1 ("–"). The caller passes its Loop or Shuttle capacity (the tram keeps its own O2 Running test
        /// as well); a carrying row gets its argument back bit for bit (HK-1).</summary>
        public static float LiftCapacityShown(float capacity, bool carries)
        {
            return carries ? capacity : -1f;
        }

        public static int OpenSharePct(double runsValue, double runsValueFull)
        {
            if (!(runsValueFull > 0.0) || !IsFiniteD(runsValueFull))
                return -1;
            double pct = 100.0 * runsValue / runsValueFull;
            if (!IsFiniteD(pct))
                return -1;
            return (int)Math.Round(ClampD(pct, 0.0, 100.0), MidpointRounding.AwayFromZero);
        }

        /// <summary>The game's factor effect (EfficiencySection.cs:99-127): max(−99, round(100f × e) − 100),
        /// the multiply in float, Math.Round(double) half to even (= Unity.Mathematics math.round); −100
        /// when e is not &gt; 0 (NaN too).</summary>
        public static int FactorEffectPct(float efficiency)
        {
            if (!(efficiency > 0f))
                return -100;
            float scaled = (float)(100f * efficiency);   // float, as the game's math.round(100f * e)
            double rounded = Math.Round((double)scaled);
            if (!(rounded <= 1000000.0))
                rounded = 1000000.0;                    // a huge or infinite factor: no int overflow
            return Math.Max(-99, (int)rounded - 100);
        }

        /// <summary>The share of the current calendar month the ledger's "this month" covers. Not since
        /// load: the elapsed fraction of the month; since load: also at most the frames simulated since
        /// the load over one month's frames. 0 for a non-finite date.</summary>
        public static float CountedFraction(bool thisMonthSinceLoad, float normalizedDate, uint framesSinceLoad, int daysPerYear)
        {
            if (!IsFiniteF(normalizedDate))
                return 0f;
            double m = normalizedDate * 12.0;
            double frac = m - Math.Floor(m);
            if (!thisMonthSinceLoad)
                return (float)frac;
            double monthFrames = TicksPerDay * (double)Math.Max(1, daysPerYear) / 12.0;
            return (float)Math.Min(frac, framesSinceLoad / monthFrames);
        }

        /// <summary>ch5 §5.5: last month when it was counted in full (not estimated); else this month
        /// projected from at least MinCountedFraction of it (estimated); else −1 (estimated, unknown).</summary>
        public static int TicketIncomeMonth(bool lastMonthFull, long incomeLast, int paidLast, long incomeThis, int paidThis,
                                            float countedFraction, out int paidMonth, out bool estimated)
        {
            if (lastMonthFull)
            {
                paidMonth = Math.Max(0, paidLast);
                estimated = false;
                return ClampInt(Math.Max(0L, incomeLast));
            }
            estimated = true;
            if (countedFraction >= MinCountedFraction)
            {
                double f = countedFraction;
                paidMonth = ClampInt(Math.Round(Math.Max(0, paidThis) / f, MidpointRounding.AwayFromZero));
                return ClampInt(Math.Round(Math.Max(0L, incomeThis) / f, MidpointRounding.AwayFromZero));
            }
            paidMonth = -1;
            return -1;
        }

        /// <summary>B1 (header "ONE NET"): THE month net = Σ max(0, income term) − Σ max(0, cost term) of <paramref name="m"/>,
        /// clamped to int. Income: lift tickets, lodge spend, lessons, passes sold this month (all BOOKED). Costs: lift charge and
        /// run upkeep (BOOKED), buildings and wages (RATE SNAPSHOTS: × MonthShare in the month in progress, the rate at the roll for
        /// a closed month). <paramref name="estimated"/> = a term is unknown (&lt; 0, read as 0).</summary>
        public static int NetMonth(in HubMonthMoney m, out bool estimated)
        {
            estimated = m.TicketIncome < 0 || m.LodgeIncome < 0 || m.LessonIncome < 0 || m.PassIncome < 0 ||
                        m.LiftCost < 0 || m.RunCost < 0 || m.BuildingsCost < 0 || m.WagesCost < 0;
            return ClampInt(m.Income - m.Costs);
        }

        /// <summary>B1: the terms of a ledger month with the history element's mapping (SkiResortHistoryMonth.FromLedger: ClampInt of
        /// each long, LiftCharged rounded half away from zero, not finite → 0); <paramref name="buildings"/> / <paramref name="wages"/>
        /// as given (−1 unknown).</summary>
        public static HubMonthMoney MonthMoneyOf(in SkiResortLedger.Month m, int buildings, int wages)
        {
            return new HubMonthMoney
            {
                TicketIncome = ClampInt(m.TicketIncome),
                LodgeIncome = ClampInt(m.LodgeSpendIncome),
                LessonIncome = ClampInt(m.LessonIncome),
                PassIncome = ClampInt(m.PassIncome),
                LiftCost = IsFiniteD(m.LiftCharged) ? ClampInt(Math.Round(m.LiftCharged, MidpointRounding.AwayFromZero)) : 0,
                RunCost = ClampInt(m.RunUpkeep),
                BuildingsCost = buildings < 0 ? -1 : buildings,
                WagesCost = wages < 0 ? -1 : wages,
            };
        }

        /// <summary>B1: the share of the month simulated, framesSimulated / monthFrames clamped to 0..1 (0 when either is not
        /// finite or monthFrames is not &gt; 0). The caller passes SkiGuestCounterSystem.MonthFramesSimulated and
        /// SkiMoneyRules.MonthFrames(daysPerYear) — the frames the ledger's month in progress has booked.</summary>
        public static double MonthShare(double framesSimulated, double monthFrames)
        {
            if (!IsFiniteD(framesSimulated) || !IsFiniteD(monthFrames) || !(monthFrames > 0.0))
                return 0.0;
            return ClampD(framesSimulated / monthFrames, 0.0, 1.0);
        }

        /// <summary>B1: a rate snapshot's part for the month so far: round(rate × share) half away from zero; −1 when the rate is
        /// unknown (&lt; 0); a share that is not finite reads 0, else it is clamped to 0..1.</summary>
        public static int RateSoFar(int ratePerMonth, double share)
        {
            if (ratePerMonth < 0)
                return -1;
            double s = IsFiniteD(share) ? ClampD(share, 0.0, 1.0) : 0.0;
            return ClampInt(Math.Round(ratePerMonth * s, MidpointRounding.AwayFromZero));
        }

        /// <summary>The model's A with <paramref name="lodges"/> active A-frame lodges, everything else as in
        /// <paramref name="r"/> (ch5 §6.2; the problems' "if fixed" numbers — IfOneLodge). Clamp before rounding.
        /// Catalog Phase B (round-2-contract-skeptic.md MUST 1; was `r.AOpen − r.LodgeBase × r.LodgeShare +
        /// LodgeBaseOf(n) × r.LodgeShare`, HubRules.cs:381): r.AOpen − r.LodgeApplied + LodgeBaseOf(n) × the A-frame's
        /// season share (SummerLodgeShare in summer mode, else 1) — no base × share reconstruction, so a summer area
        /// with no lodge keeps today's value whatever LodgeShare the Result reports.</summary>
        public static int AttractionWithLodges(in SkiResortScore.Result r, int lodges)
        {
            if (lodges <= 0)
                return 0;
            bool summer = (r.Flags & SkiResortScore.ScoreFlags.SummerMode) != 0;
            double share = summer ? SkiResortScore.SummerLodgeShare : 1.0;
            return AttractionWithLodgeApplied(in r, SkiResortScore.LodgeBaseOf(lodges) * share);
        }

        /// <summary>Catalog Phase B (round-2-contract §6.1, seam S4; MUST 1): the model's A with the area's applied
        /// lodge base replaced by <paramref name="lodgeApplied"/> (a season value: the ladder's applied value of the
        /// lodges the "if fixed" assumes — SkiResortStatsSystem's IfLodgesOn passes SkiResortScore.LodgeBase over every
        /// lodge of the area switched on), everything else as in <paramref name="r"/>: (r.AOpen − r.LodgeApplied +
        /// lodgeApplied) × efficiency, clamped to 0..MaxAttraction BEFORE RoundToEven. A non-finite or negative
        /// <paramref name="lodgeApplied"/> reads 0.</summary>
        public static int AttractionWithLodgeApplied(in SkiResortScore.Result r, double lodgeApplied)
        {
            double eff = r.Efficiency > 0.0 ? r.Efficiency : 0.0;   // NaN → 0
            double applied = IsFiniteD(lodgeApplied) && lodgeApplied > 0.0 ? lodgeApplied : 0.0;
            double aOpen = r.AOpen - FiniteOr0(r.LodgeApplied) + applied;
            double raw = aOpen * eff;
            if (!(raw >= 0.0))
                raw = 0.0;
            if (raw > SkiResortScore.MaxAttraction)
                raw = SkiResortScore.MaxAttraction;
            return SkiResortScore.RoundToEven(raw);
        }

        /// <summary>D37: true = HINT[LiftNoPair] (place the other station first). False when the shown area
        /// has a base and a top station with a line to draw or finish: one row with both stations live in state
        /// 2, 3 or 4, OR (E1 fixer, e1-spec §8.9 F9) a lone base and a lone top on DIFFERENT rows. The census
        /// never builds a state-4 row with both stations — each untouched station is its own StationsOnly
        /// row (LiftCensus.cs rows step 5) and an Unfinished row carries at most one — so the pair has to be
        /// found across rows. A lone station = state 3 or 4 with exactly one of HasBase / HasTop.</summary>
        public static bool LiftNoPair(IReadOnlyList<HubLiftHintRow> rowsOfShownArea)
        {
            if (rowsOfShownArea == null)
                return true;
            bool loneBase = false, loneTop = false;
            for (int i = 0; i < rowsOfShownArea.Count; i++)
            {
                HubLiftHintRow row = rowsOfShownArea[i];
                if (row.HasBase && row.HasTop && (row.State == 2 || row.State == 3 || row.State == 4))
                    return false;
                if (row.State == 3 || row.State == 4)
                {
                    if (row.HasBase && !row.HasTop)
                        loneBase = true;
                    else if (row.HasTop && !row.HasBase)
                        loneTop = true;
                }
            }
            return !(loneBase && loneTop);
        }

        /// <summary>The receipt's "Lodge base": the ladder's season value r.LodgeApplied (non-finite → 0). Catalog Phase B
        /// (round-2-contract §6.1, seam S4; was r.LodgeBase × r.LodgeShare, HubRules.cs:420 — equal for A-frames, where
        /// LodgeApplied = LodgeBase × 0.5 in summer exactly). LodgePart + Terrain = AOpen (= LodgeApplied + K√S).</summary>
        public static double LodgePart(in SkiResortScore.Result r)
        {
            return FiniteOr0(r.LodgeApplied);
        }

        /// <summary>The receipt's "Terrain": r.K × √r.S (S ≤ 0 or NaN → 0). LodgePart + Terrain = AOpen.</summary>
        public static double Terrain(in SkiResortScore.Result r)
        {
            if (!(r.S > 0.0))
                return 0.0;
            return FiniteOr0(r.K * Math.Sqrt(r.S));
        }

        /// <summary>A run's value fully open: BandValue × SizeFactor × TypeFactor × ServedFactor (non-finite → 0).</summary>
        public static double RunValueFull(in SkiResortScore.RunPart p)
        {
            return FiniteOr0(p.BandValue * p.SizeFactor * p.TypeFactor * p.ServedFactor);
        }

        /// <summary>round(100 × clamp01(share)) half away from zero; non-finite → −1.</summary>
        public static int Percent(double share)
        {
            if (!IsFiniteD(share))
                return -1;
            return (int)Math.Round(100.0 * ClampD(share, 0.0, 1.0), MidpointRounding.AwayFromZero);
        }

        /// <summary>The day-mean temperature at <paramref name="topM"/>: dayMeanC − (topM − baseHeight) ×
        /// lapsePerM (SnowLineSystem.GetLocalTemperature's expression with the day mean); NaN when any input
        /// is not finite.</summary>
        public static float TopTempC(float dayMeanC, float topM, float baseHeight, float lapsePerM)
        {
            if (!IsFiniteF(dayMeanC) || !IsFiniteF(topM) || !IsFiniteF(baseHeight) || !IsFiniteF(lapsePerM))
                return float.NaN;
            return dayMeanC - (topM - baseHeight) * lapsePerM;
        }

        /// <summary>Rule 7's guards (D36, ch5 C5), in this order.</summary>
        public static FollowVerdict FollowCheck(bool hubOpen, bool ruleSwitchedThisPass, float nowS, float lastFollowS, float lastUserSelectS)
        {
            if (!hubOpen)
                return FollowVerdict.HubClosed;
            if (ruleSwitchedThisPass)
                return FollowVerdict.RuleSwitched;
            if (nowS - lastUserSelectS < FollowQuietAfterUserS)
                return FollowVerdict.UserJustSelected;
            if (nowS - lastFollowS < FollowMinIntervalS)
                return FollowVerdict.TooSoon;
            return FollowVerdict.Follow;
        }

        /// <summary>Round S1 (ch4 §A.3, s1-spec §2.9 / N9): the Guests view's ski-day state, first match wins.
        /// <paramref name="off"/> = both ski-day settings 0; <paramref name="hasView"/> = SkiDaySystem armed with a
        /// view of the area; <paramref name="scoreKnown"/>, <paramref name="signRunning"/>, <paramref
        /// name="runsCounted"/> from the attraction view (a result and no first-write hold, efficiency &gt; 0, the
        /// Result's RunsCounted); the rest from SkiDayAreaView. A capacity of 0 reads Full, as in the simulation;
        /// a negative capacity or count means "before the first pass" (Unknown).</summary>
        public static HubSkiDayState SkiDayStateOf(bool off, bool hasView, bool scoreKnown, bool signRunning,
                                                   int runsCounted, int targetLodges, bool claimHours, int now, int cap)
        {
            if (off)
                return HubSkiDayState.Off;
            if (!hasView || !scoreKnown)
                return HubSkiDayState.Unknown;
            if (!signRunning)
                return HubSkiDayState.SignOff;
            if (runsCounted <= 0)
                return HubSkiDayState.NoRun;
            if (targetLodges <= 0)
                return HubSkiDayState.NoLodge;
            if (!claimHours)
                return HubSkiDayState.Night;
            if (cap < 0 || now < 0)
                return HubSkiDayState.Unknown;
            return now >= cap ? HubSkiDayState.Full : HubSkiDayState.Room;
        }

        // ── net48-safe helpers (internal: HubSnowSums and HubProblems use them) ─────────────────────

        internal static bool IsFiniteD(double x) => !double.IsNaN(x) && !double.IsInfinity(x);

        internal static bool IsFiniteF(float x) => !float.IsNaN(x) && !float.IsInfinity(x);

        internal static double ClampD(double x, double lo, double hi) => x < lo ? lo : x > hi ? hi : x;

        private static double FiniteOr0(double x) => IsFiniteD(x) ? x : 0.0;

        private static int ClampInt(double x)
        {
            if (!(x > int.MinValue))
                return double.IsNaN(x) ? 0 : int.MinValue;
            return x >= int.MaxValue ? int.MaxValue : (int)x;
        }

        private static int ClampInt(long x)
        {
            return x > int.MaxValue ? int.MaxValue : x < int.MinValue ? int.MinValue : (int)x;
        }
    }

    /// <summary>The problems band of one area (ch5 §6.2, §7.3). Header RULES.</summary>
    public static class HubProblems
    {
        /// <summary>Clears <paramref name="output"/>, then the band rows (sorted, at most MaxBandRows), then
        /// the Factor rows. No allocation once <paramref name="output"/> has grown.</summary>
        public static void Build(in HubProblemInput input, List<HubProblem> output)
        {
            output.Clear();

            // 1. The Not linked entry: two codes only.
            if (input.Unlinked)
            {
                if (input.Runs > 0 || input.LiftRows > 0)
                    output.Add(Row(HubProblemCode.Unlinked, 3, buildItem: "sign"));
                if (input.FreeLodges > 0)
                    output.Add(Row(HubProblemCode.FreeLodges, 1, count: input.FreeLodges));
                if (input.FreeSchools > 0)   // T100: a top-level school beyond every sign's tether
                    output.Add(Row(HubProblemCode.SchoolsNotLinked, 1, count: input.FreeSchools));
                SortAndCut(output);
                return;
            }

            // 2. Lodges.
            if (input.Lodges == 0)
                output.Add(Row(HubProblemCode.NoLodge, 3, ifFixed: input.IfOneLodge, buildItem: "lodge"));
            else if (input.LodgesActive == 0)
                output.Add(Row(HubProblemCode.LodgesAllOff, 3, count: input.Lodges, ifFixed: input.IfLodgesOn));
            else if (input.LodgesActive < input.Lodges)
                output.Add(Row(HubProblemCode.LodgesSomeOff, 1, count: input.Lodges - input.LodgesActive));

            // 2b. Round F7 D5: road edges (the stats rebuild reads Building.m_RoadEdge). The game shows no warning
            // for a sign off the road (field-test-7 sign-utilities.md §5); it then gets no power, water or sewage.
            // A lodge with no road edge has no walking target for guests (ped-access.md §4.2). Lodge severity
            // mirrors LodgesAllOff / LodgesSomeOff: every lodge 3, some 1. Count = lodges with no road (≤ Lodges).
            if (input.SignNoRoad)
                output.Add(Row(HubProblemCode.SignNoRoad, 3));
            // T100: a top-level ski school with no road edge joins the count (severity 1 on its own: it is not a lodge, so it
            // never makes "every lodge" true).
            int lodgesNoRoad = Math.Min(input.LodgesNoRoad, input.Lodges);
            int buildingsNoRoad = lodgesNoRoad + Math.Max(0, input.SchoolsNoRoad);
            if (buildingsNoRoad > 0)
                output.Add(Row(HubProblemCode.LodgeNoRoad, (byte)(lodgesNoRoad > 0 && lodgesNoRoad >= input.Lodges ? 3 : 1),
                    count: buildingsNoRoad));

            // 3. Efficiency (the worst factor named; the rest counted). Round S1 F1 (ch4 §C.3): shown at or below
            // EfficiencyProblemPct, and held while the band showed it on the area's previous build until the percent
            // reaches EfficiencyClearPct (field test 8: the row flapped 98 times around 90 %). The state is the stats
            // system's (EfficiencyRowWasOn); the rule stays pure.
            // "and {N} more" counts the factors the tooltip lists, i.e. step 9's Factor rows (R-Q2 extended to the band
            // row, fixer pass, review HU-1): the worst factor is first, so it is a listed one whenever any is listed.
            // With none listed (all under 5 points) the row keeps counting every factor, so it never presents one
            // small factor as the whole loss.
            int factors = input.Factors != null ? input.Factors.Count : 0;
            int listed = 0;
            for (int i = 0; i < factors; i++)
            {
                if (input.Factors[i].EffectPct <= -HubRules.FactorRowMinLossPct)
                    listed++;
            }
            bool showEfficiency = input.EfficiencyPct >= 0 &&
                                  (input.EfficiencyPct <= HubRules.EfficiencyProblemPct ||
                                   (input.EfficiencyRowWasOn && input.EfficiencyPct < HubRules.EfficiencyClearPct));
            if (showEfficiency)
            {
                output.Add(Row(HubProblemCode.Efficiency, (byte)(input.EfficiencyPct == 0 ? 3 : 2), pct: input.EfficiencyPct,
                    factor: factors > 0 ? input.Factors[0].Name ?? "" : "",
                    more: Math.Max(0, (listed > 0 ? listed : factors) - 1), ifFixed: input.AttractionFull));
            }

            // 4. Lifts, in `lifts` order.
            // E1 fixer (e1-spec §8.9 F9): the census makes every untouched station its own single-station
            // StationsOnly row (state 4) and an Unfinished row (state 3) carries at most one station, so a
            // placed base and a placed top are never ONE row. Pre-scan: the i-th lone base of state 4 pairs
            // with the i-th lone top of state 4 — the pair is one Detail 0 row ("draw the lift line", build
            // "lift"), and the other station's row is not listed. A lone state-4 station left over
            // whose other kind waits on an Unfinished line's end also reads Detail 0 (finish the line to it).
            // Only a station with no partner anywhere keeps Detail 1 / 2.
            // E1 fixer pass 2 (e1-spec §8.10 G4): the pair row sits at the row whose NAME the finished lift
            // keeps (the stats system's lift-name rule: the base's custom name, else the top's custom name,
            // else "Lift N" numbered by top-station height with rows without a top last, SkiResortAggregate
            // .LiftOrdinals) — the base's row when the base has a custom name, else the top's row. At the
            // base's row the band named "Lift 2" a lift that the hub calls "Lift 1" once its line is drawn.
            // Catalog Phase B (round-2-contract §6.1): the pairing runs PER LIFT TYPE — a lone fixed-grip base never
            // pairs with a lone detachable top (that line would be Mixed). For a fixed-grip-only area (every Type 0)
            // the counts, the pairs and the rows are exactly today's. A Mixed row is not an unfinished line of its
            // stations' kind, so it does not count as one. No allocation: per-type counts are rescans (an area has a
            // handful of lift rows).
            int lifts = input.Lifts != null ? input.Lifts.Count : 0;
            for (int k = 0; k < lifts; k++)
            {
                HubProblemLift l = input.Lifts[k];
                // Catalog Phase B (round-2-contract §6.1): the build items come from the row's lift type (the fixed-grip
                // row's are today's "lift" / "topStation" / "baseStation"; an unknown type falls back to them).
                string lineItem = LineItem(l.Type);
                if (l.Mixed && l.State != 0 && l.State != 1)
                {
                    // Catalog Phase B (D18): a component that mixes our lift types is Unfinished in the census; the band
                    // names the cause (redraw the line with the stations' kind) instead of "unfinished".
                    output.Add(Row(HubProblemCode.MixedLiftTypes, 2, liftIndex: k, buildItem: lineItem));
                    continue;
                }
                switch (l.State)
                {
                    case 0:
                    case 1:
                        if (!l.Powered)
                            output.Add(Row(HubProblemCode.LiftNoPower, 2, liftIndex: k));
                        else if (l.State == 1)
                            output.Add(Row(HubProblemCode.LiftChairsStopped, 1, liftIndex: k));
                        break;
                    case 2:
                        output.Add(Row(HubProblemCode.LiftSectionMissing, 2, liftIndex: k, buildItem: lineItem));
                        break;
                    case 3:
                        output.Add(Row(HubProblemCode.LiftUnfinished, 2, liftIndex: k, buildItem: lineItem));
                        break;
                    case 4:
                        if (l.HasBase && l.HasTop)   // defensive: the census does not build this row (above)
                            output.Add(Row(HubProblemCode.StationsOnly, 1, liftIndex: k, detail: 0, buildItem: lineItem));
                        else if (l.HasBase)
                        {
                            int pairs = Math.Min(CountLone(input.Lifts, l.Type, true, lifts), CountLone(input.Lifts, l.Type, false, lifts));
                            int seenBases = CountLone(input.Lifts, l.Type, true, k + 1);   // this base's 1-based rank
                            if (seenBases <= pairs)
                            {
                                if (l.CustomName)   // pass 2: the finished lift keeps the base's name
                                    output.Add(Row(HubProblemCode.StationsOnly, 1, liftIndex: k, detail: 0, buildItem: lineItem));
                            }
                            else if (UnfinishedLine(input.Lifts, l.Type, false))
                                output.Add(Row(HubProblemCode.StationsOnly, 1, liftIndex: k, detail: 0, buildItem: lineItem));
                            else
                                output.Add(Row(HubProblemCode.StationsOnly, 1, liftIndex: k, detail: 1,
                                    buildItem: StationItem(l.Type, StationRole.Top)));
                        }
                        else if (l.HasTop)
                        {
                            int pairs = Math.Min(CountLone(input.Lifts, l.Type, true, lifts), CountLone(input.Lifts, l.Type, false, lifts));
                            int seenTops = CountLone(input.Lifts, l.Type, false, k + 1);    // this top's 1-based rank
                            if (seenTops <= pairs)
                            {
                                // pass 2: listed here unless its pair's base has a custom name (listed there).
                                if (!LoneBaseNamed(input.Lifts, l.Type, seenTops))
                                    output.Add(Row(HubProblemCode.StationsOnly, 1, liftIndex: k, detail: 0, buildItem: lineItem));
                                break;
                            }
                            if (UnfinishedLine(input.Lifts, l.Type, true))
                                output.Add(Row(HubProblemCode.StationsOnly, 1, liftIndex: k, detail: 0, buildItem: lineItem));
                            else
                                output.Add(Row(HubProblemCode.StationsOnly, 1, liftIndex: k, detail: 2,
                                    buildItem: StationItem(l.Type, StationRole.Base)));
                        }
                        break;
                }
            }

            // 5-8. Lifts and runs of the area.
            if (input.LiftRows == 0)
                output.Add(Row(HubProblemCode.NoLift, 1, pct: input.BackcountryPct, buildItem: "baseStation"));
            if (input.Runs == 0)
                output.Add(Row(HubProblemCode.NoRuns, 1));
            if (input.RunsUnserved > 0 && input.LiftRows > 0 && input.BackcountryPct < 100)
                output.Add(Row(HubProblemCode.RunsUnserved, 1, count: input.RunsUnserved, pct: input.BackcountryPct));
            if (input.RunsUnrateable > 0)
                output.Add(Row(HubProblemCode.RunsUnrateable, 1, count: input.RunsUnrateable));
            if (input.RunsRating > 0)
                output.Add(Row(HubProblemCode.RunsRating, 1, count: input.RunsRating));
            if (input.RunsFlat > 0)
                output.Add(Row(HubProblemCode.RunsFlat, 1, count: input.RunsFlat));

            // 8b. Round S1 D28 (ch4 §C.3): the ski-day rows. The stats system sets the inputs only while ski days are
            // on and the area has a SkiDaySystem view. CannotReach is severity 2 (warning glyph); the two lodge rows
            // are dim (1), counted up to the area's lodges, so a stray count never outnumbers them.
            if (input.SkiDaysCannotReach)
                output.Add(Row(HubProblemCode.SkiDaysCannotReach, 2));
            int lodgesNoParking = Math.Min(input.LodgesNoParking, input.Lodges);
            if (lodgesNoParking > 0)
                output.Add(Row(HubProblemCode.NoParkingNearLodge, 1, count: lodgesNoParking));
            int lodgesFar = Math.Min(input.LodgesNotSlopeside, input.Lodges);
            // Round M1 (D9): "far from the runs" needs runs; an area without any already shows NoRuns and sends no
            // ski days (SkiDayRules.AreaStateOf).
            if (lodgesFar > 0 && input.Runs > 0)
                output.Add(Row(HubProblemCode.LodgeNotSlopeside, 1, count: lodgesFar));

            // 8c. Catalog Phase B (round-2-contract §6.1): summit lodges no working lift top reaches (they count their
            // summit-served summer share only within 300 m, R11), counted up to the area's lodges; the ski school
            // without a counted green run (its +12 and its demand factor need one, D13). Both dim (severity 1).
            int summitNotServed = Math.Min(input.SummitLodgesNotServed, Math.Max(0, input.Lodges));
            if (summitNotServed > 0)
                output.Add(Row(HubProblemCode.SummitLodgeNotLiftServed, 1, count: summitNotServed));
            if (input.SchoolNoGreenRun)
                output.Add(Row(HubProblemCode.SchoolNoGreenRun, 1));

            SortAndCut(output);

            // 9. Factor rows (only with a result), input order. Round S1 F1b (orchestrator ruling R-Q2): only a factor
            // that costs at least FactorRowMinLossPct points (EffectPct <= −5); field test 8's churn was mostly small
            // factors near 0 % coming and going (ft8-log-audit V.2 #1). A pure threshold, no dwell. The Overview's
            // factor list and the Efficiency row's tooltip read these rows (hubRules.ts factorRowsOf), so they follow.
            if (input.EfficiencyPct >= 0)
            {
                for (int i = 0; i < factors; i++)
                {
                    HubProblemFactor f = input.Factors[i];
                    if (f.EffectPct <= -HubRules.FactorRowMinLossPct)
                        output.Add(Row(HubProblemCode.Factor, 0, pct: f.EffectPct, factor: f.Name ?? ""));
                }
            }
        }

        /// <summary>E1 fixer pass 2: the CustomName of the <paramref name="n"/>-th (1-based) lone base of state
        /// 4 in `lifts` order (false when there is none). O(lifts) per call, no allocation; called once per
        /// paired top, and an area has a handful of lift rows.</summary>
        private static bool LoneBaseNamed(List<HubProblemLift> lifts, byte type, int n)
        {
            // Catalog Phase B: the n-th lone base OF THE SAME LIFT TYPE (pairing per type, Build step 4).
            int seen = 0;
            for (int k = 0; k < lifts.Count; k++)
            {
                HubProblemLift l = lifts[k];
                if (l.State != 4 || !l.HasBase || l.HasTop || l.Type != type)
                    continue;
                if (++seen == n)
                    return l.CustomName;
            }
            return false;
        }

        /// <summary>Catalog Phase B: lone stations (state 4, exactly one of HasBase / HasTop) of lift type
        /// <paramref name="type"/>, bases when <paramref name="bases"/> else tops, among the first
        /// <paramref name="upTo"/> rows. No allocation.</summary>
        private static int CountLone(List<HubProblemLift> lifts, byte type, bool bases, int upTo)
        {
            int n = 0;
            for (int k = 0; k < upTo && k < lifts.Count; k++)
            {
                HubProblemLift l = lifts[k];
                if (l.State != 4 || l.Type != type)
                    continue;
                if (bases ? l.HasBase && !l.HasTop : l.HasTop && !l.HasBase)
                    n++;
            }
            return n;
        }

        /// <summary>Catalog Phase B: an Unfinished (state 3), not Mixed, row of lift type <paramref name="type"/> that
        /// carries exactly one station — its base when <paramref name="fromBase"/>, else its top (a line drawn from that
        /// station, waiting for the other kind). The E1 F9 rule per type.</summary>
        private static bool UnfinishedLine(List<HubProblemLift> lifts, byte type, bool fromBase)
        {
            for (int k = 0; k < lifts.Count; k++)
            {
                HubProblemLift l = lifts[k];
                if (l.State != 3 || l.Mixed || l.Type != type)
                    continue;
                if (fromBase ? l.HasBase && !l.HasTop : l.HasTop && !l.HasBase)
                    return true;
            }
            return false;
        }

        /// <summary>Catalog Phase B: the row type's LINE build item ("lift", "liftDetachable", …); an unknown type reads
        /// the fixed-grip "lift" (SkiCatalog.LineItemOf returns null out of range).</summary>
        private static string LineItem(byte type)
        {
            return SkiCatalog.LineItemOf(type) ?? SkiCatalog.FixedGrip.BuildItemId;
        }

        /// <summary>Catalog Phase B: the row type's base / top STATION build item ("baseStation", "topStationGondola",
        /// …); an unknown type reads the fixed-grip station's.</summary>
        private static string StationItem(byte type, StationRole role)
        {
            return SkiCatalog.StationItemOf(type, role) ?? SkiCatalog.StationItemOf(0, role) ??
                   (role == StationRole.Base ? "baseStation" : "topStation");
        }

        private static HubProblem Row(HubProblemCode code, byte severity, int liftIndex = -1, int count = -1, int pct = -1,
                                      int more = 0, int detail = 0, int ifFixed = -1, string factor = "", string buildItem = "")
        {
            return new HubProblem
            {
                Code = code, Severity = severity, LiftIndex = liftIndex, Count = count, Pct = pct, More = more,
                Detail = detail, IfFixed = ifFixed, Factor = factor ?? "", BuildItem = buildItem ?? "",
            };
        }

        /// <summary>Stable insertion sort by (severity descending, code ascending); equal keys keep their
        /// insertion order. Then the band is cut to MaxBandRows.</summary>
        private static void SortAndCut(List<HubProblem> rows)
        {
            for (int i = 1; i < rows.Count; i++)
            {
                HubProblem x = rows[i];
                int j = i - 1;
                while (j >= 0 && After(rows[j], x))
                {
                    rows[j + 1] = rows[j];
                    j--;
                }
                rows[j + 1] = x;
            }
            if (rows.Count > HubRules.MaxBandRows)
                rows.RemoveRange(HubRules.MaxBandRows, rows.Count - HubRules.MaxBandRows);
        }

        /// <summary>True when <paramref name="a"/> sorts strictly after <paramref name="b"/>.</summary>
        private static bool After(HubProblem a, HubProblem b)
        {
            if (a.Severity != b.Severity)
                return a.Severity < b.Severity;
            return a.Code > b.Code;
        }
    }
}
