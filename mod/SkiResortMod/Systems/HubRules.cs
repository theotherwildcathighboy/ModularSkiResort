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
//   Season            gating off GatingOff; no run / unknown top temperature None; natural = T <= freezing,
//                     gun = a gun run && T <= freezing + margin (both inclusive); with an open run: natural
//                     SnowSeason, else gun SnowGunsOnly, else Thawing; none open: natural or gun
//                     WaitingForSnow, else TooWarm
//   NextStep          lodge, then a complete lift, then a run, else Done
//   LiftStatusOf      stats state 0 powered Running / NoPower; 1 powered ChairsStopped / NoPower;
//                     2 SectionMissing; anything else Unfinished
//   RideTimeS         loop / 2 / ChairSpeedMps (a chair rides half the loop); −1 without a loop
//   OpenSharePct      round(100 × value / full) half away from zero, 0..100; −1 when full is not > 0
//   FactorEffectPct   the game's panel number (Game.UI.InGame/EfficiencySection.cs:99-127):
//                     max(−99, round(100f × e) − 100) with the multiply in FLOAT and Math.Round(double)
//                     half to even (= math.round); −100 when e is not > 0
//   CountedFraction   the share of the calendar month the ledger's "this month" covers (since load: also
//                     capped by the frames simulated since the load); 0 for a non-finite date
//   TicketIncomeMonth last month when it was counted in full; else this month projected from at least
//                     MinCountedFraction of a month; else −1 (unknown)
//   NetMonth          max(0, tickets) − round(run + lift + buildings + wages costs), each cost < 0 or
//                     non-finite read as 0; estimated when the income is projected or unknown, or the
//                     building bill is unknown (−1)
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
// PURE FILE: `using System;` and `using System.Collections.Generic;` only, plus SkiResortScore (pure,
// ch1). No Entities, no Game, no Unity.Mathematics. net48 traps: no Math.Clamp, MathF, double.IsFinite,
// Span<T>; private Clamp and IsFinite below. No allocation once the caller's lists have grown.

using System;
using System.Collections.Generic;

namespace SkiResortMod.Systems
{
    /// <summary>bindings.ts RESORT_STATUS (gate R17: literal values).</summary>
    public enum ResortStatus : byte { NoRuns = 0, Closed = 1, Partly = 2, Open = 3 }

    /// <summary>bindings.ts OPEN_STATE (gate R17).</summary>
    public enum RunOpenState : sbyte { None = -1, Closed = 0, Partly = 1, Open = 2 }

    /// <summary>bindings.ts SEASON_HINT (gate R17).</summary>
    public enum SeasonHint : byte { None = 0, SnowSeason = 1, SnowGunsOnly = 2, Thawing = 3, WaitingForSnow = 4, TooWarm = 5, GatingOff = 6 }

    /// <summary>bindings.ts NEXT_STEP (gate R17).</summary>
    public enum NextStepKind : byte { Done = 0, Lodge = 1, Lift = 2, Run = 3 }

    /// <summary>bindings.ts LIFT_STATUS (gate R17).</summary>
    public enum HubLiftStatus : byte { Running = 0, NoPower = 1, ChairsStopped = 2, SectionMissing = 3, Unfinished = 4 }

    /// <summary>bindings.ts PROBLEM (gate R17). APPEND ONLY, never renumber.</summary>
    public enum HubProblemCode : byte
    {
        Unlinked = 0, NoLodge = 1, LodgesAllOff = 2, LodgesSomeOff = 3, Efficiency = 4, LiftNoPower = 5,
        LiftSectionMissing = 6, LiftUnfinished = 7, NoLift = 8, NoRuns = 9, StationsOnly = 10,
        LiftChairsStopped = 11, RunsUnserved = 12, RunsUnrateable = 13, RunsRating = 14, FreeLodges = 15, Factor = 16,
        RunsFlat = 17,   // second pass (e1-spec §2.5 H1): appended after Factor
        SignNoRoad = 18, LodgeNoRoad = 19,   // Round F7 D5 (field-test-7-fixes.md): appended (gate R17)
        SkiDaysCannotReach = 20, NoParkingNearLodge = 21, LodgeNotSlopeside = 22,   // Round S1 D28 (ski-days ch4 §C.1): appended
    }

    /// <summary>bindings.ts SKI_DAY_STATE (gate R17): ResortLive.skiDaysState, the Guests view's state line (Round
    /// S1, contract 4, ski-days spec ch4 §A.2). The hub's display state; SkiDaySystem keeps its own.</summary>
    public enum HubSkiDayState : byte { Unknown = 0, Off = 1, SignOff = 2, NoRun = 3, NoLodge = 4, Night = 5, Room = 6, Full = 7 }

    /// <summary>Rule 7's verdict (D36; C# only, not in the contract).</summary>
    public enum FollowVerdict : byte { Follow = 0, HubClosed = 1, RuleSwitched = 2, UserJustSelected = 3, TooSoon = 4 }

    /// <summary>One lift row of the shown area for LiftNoPair (State = the stats LIFT_STATE).</summary>
    public struct HubLiftHintRow { public int State; public bool HasBase, HasTop; }

    /// <summary>One efficiency factor below 100 % (worst first): the EfficiencyFactor enum NAME and the
    /// game's effect number (FactorEffectPct).</summary>
    public struct HubProblemFactor { public string Name; public int EffectPct; }

    /// <summary>One lift row of the area in `lifts` order (State = the stats LIFT_STATE). CustomName (E1
    /// fixer pass 2) = the row's name is a station's custom name (LiftStats.CustomName), not "Lift N".</summary>
    public struct HubProblemLift { public int State; public bool Powered, HasBase, HasTop, CustomName; }

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

    /// <summary>The hub's pure rules (ch5 §6.1-6.2). Header RULES.</summary>
    public static class HubRules
    {
        public const float ChairSpeedMps = 2.5f;        // = ChairPinSystem.ChairSpeedMps (checked at the stats arm, ch5 §5.8)
        public const float MinCountedFraction = 0.25f;  // never project tickets from less than a quarter month
        public const int EfficiencyProblemPct = 90;     // the band shows efficiency at or below this
        public const int EfficiencyClearPct = 95;       // Round S1 F1: a shown Efficiency row stays until this (FT8: 98 flaps)
        public const int FactorRowMinLossPct = 5;       // Round S1 F1b (ruling R-Q2): a Factor row only from a 5-point loss
        public const int MaxBandRows = 8;
        public const int LiftLinePerKm = 7500;          // D24: 60 per 8 m piece × 125 pieces per km (ground level)
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

        public static SeasonHint Season(bool gating, int runs, int runsOpenAny, bool anyGunRun,
                                        bool topTempKnown, float topTempC, float freezingC, float marginC)
        {
            if (!gating)
                return SeasonHint.GatingOff;
            if (runs <= 0 || !topTempKnown || float.IsNaN(topTempC))
                return SeasonHint.None;
            bool natural = topTempC <= freezingC;
            bool gun = anyGunRun && topTempC <= freezingC + marginC;
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

        public static HubLiftStatus LiftStatusOf(int statsState, bool powered)
        {
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
        /// finite loop &gt; 0.</summary>
        public static float RideTimeS(float loopM)
        {
            if (!IsFiniteF(loopM) || !(loopM > 0f))
                return -1f;
            return loopM / 2f / ChairSpeedMps;
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

        /// <summary>ch5 §6.2: max(0, tickets) − round(costs); estimated when the income is projected or
        /// unknown, or buildings / wages are unknown (−1).</summary>
        public static int NetMonth(int ticketIncomeMonth, bool incomeEstimated, float runCosts, float liftCosts,
                                   float buildings, float wages, out bool estimated)
        {
            estimated = incomeEstimated || ticketIncomeMonth < 0 || buildings < 0f || wages < 0f;
            double costs = Cost(runCosts) + Cost(liftCosts) + Cost(buildings) + Cost(wages);
            double rounded = Math.Round(costs, MidpointRounding.AwayFromZero);
            long net = Math.Max(0, ticketIncomeMonth) - (long)Math.Min(rounded, int.MaxValue);
            return ClampInt(net);
        }

        /// <summary>The model's A with <paramref name="lodges"/> active lodges, everything else as in
        /// <paramref name="r"/> (ch5 §6.2; the problems' "if fixed" numbers). Clamp before rounding.</summary>
        public static int AttractionWithLodges(in SkiResortScore.Result r, int lodges)
        {
            if (lodges <= 0)
                return 0;
            double eff = r.Efficiency > 0.0 ? r.Efficiency : 0.0;   // NaN → 0
            double aOpen = r.AOpen - r.LodgeBase * r.LodgeShare + SkiResortScore.LodgeBaseOf(lodges) * r.LodgeShare;
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

        /// <summary>The receipt's "Lodge base": r.LodgeBase × r.LodgeShare (non-finite → 0).</summary>
        public static double LodgePart(in SkiResortScore.Result r)
        {
            return FiniteOr0(r.LodgeBase * r.LodgeShare);
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

        private static double Cost(float x) => IsFiniteF(x) && x > 0f ? x : 0.0;

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
            int lodgesNoRoad = Math.Min(input.LodgesNoRoad, input.Lodges);
            if (lodgesNoRoad > 0)
                output.Add(Row(HubProblemCode.LodgeNoRoad, (byte)(lodgesNoRoad >= input.Lodges ? 3 : 1), count: lodgesNoRoad));

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
            int lifts = input.Lifts != null ? input.Lifts.Count : 0;
            int loneBases = 0, loneTops = 0;
            bool unfinishedBase = false, unfinishedTop = false;
            for (int k = 0; k < lifts; k++)
            {
                HubProblemLift l = input.Lifts[k];
                bool baseOnly = l.HasBase && !l.HasTop, topOnly = l.HasTop && !l.HasBase;
                if (l.State == 4)
                {
                    if (baseOnly)
                        loneBases++;
                    else if (topOnly)
                        loneTops++;
                }
                else if (l.State == 3)
                {
                    unfinishedBase |= baseOnly;
                    unfinishedTop |= topOnly;
                }
            }
            int pairs = Math.Min(loneBases, loneTops);
            int seenBases = 0, seenTops = 0;
            for (int k = 0; k < lifts; k++)
            {
                HubProblemLift l = input.Lifts[k];
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
                        output.Add(Row(HubProblemCode.LiftSectionMissing, 2, liftIndex: k, buildItem: "lift"));
                        break;
                    case 3:
                        output.Add(Row(HubProblemCode.LiftUnfinished, 2, liftIndex: k, buildItem: "lift"));
                        break;
                    case 4:
                        if (l.HasBase && l.HasTop)   // defensive: the census does not build this row (above)
                            output.Add(Row(HubProblemCode.StationsOnly, 1, liftIndex: k, detail: 0, buildItem: "lift"));
                        else if (l.HasBase)
                        {
                            seenBases++;
                            if (seenBases <= pairs)
                            {
                                if (l.CustomName)   // pass 2: the finished lift keeps the base's name
                                    output.Add(Row(HubProblemCode.StationsOnly, 1, liftIndex: k, detail: 0, buildItem: "lift"));
                            }
                            else if (unfinishedTop)
                                output.Add(Row(HubProblemCode.StationsOnly, 1, liftIndex: k, detail: 0, buildItem: "lift"));
                            else
                                output.Add(Row(HubProblemCode.StationsOnly, 1, liftIndex: k, detail: 1, buildItem: "topStation"));
                        }
                        else if (l.HasTop)
                        {
                            seenTops++;
                            if (seenTops <= pairs)
                            {
                                // pass 2: listed here unless its pair's base has a custom name (listed there).
                                if (!LoneBaseNamed(input.Lifts, seenTops))
                                    output.Add(Row(HubProblemCode.StationsOnly, 1, liftIndex: k, detail: 0, buildItem: "lift"));
                                break;
                            }
                            if (unfinishedBase)
                                output.Add(Row(HubProblemCode.StationsOnly, 1, liftIndex: k, detail: 0, buildItem: "lift"));
                            else
                                output.Add(Row(HubProblemCode.StationsOnly, 1, liftIndex: k, detail: 2, buildItem: "baseStation"));
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
        private static bool LoneBaseNamed(List<HubProblemLift> lifts, int n)
        {
            int seen = 0;
            for (int k = 0; k < lifts.Count; k++)
            {
                HubProblemLift l = lifts[k];
                if (l.State != 4 || !l.HasBase || l.HasTop)
                    continue;
                if (++seen == n)
                    return l.CustomName;
            }
            return false;
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
