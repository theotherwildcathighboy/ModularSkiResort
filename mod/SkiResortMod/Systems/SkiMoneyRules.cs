// SkiMoneyRules — the PURE money rules of Round E1 (docs/research/econ-round/spec/ch4-money.md §6.1;
// decisions D23 lift running charge, D26 lift tickets). Consumers: SkiLiftChargeSystem (LiftBillingOf,
// LiftAreaRunOpen, LiftRate, LiftOperating, Accrue, AccruedShare), SkiLiftTicketSystem (ClampTicketPrice,
// TicketGateOf, TicketsPaid — from its Burst job), SkiGuestCounterSystem (MonthKey), SkiResortStatsSystem /
// HubRules (gate, price, item rates), SkiResortAttractivenessSystem (MonthKey / YearOfKey / MonthOfKey for its
// [cost] clock, Round M2 D8), the settings (TicketPriceDefault); SkiGuestCounterSystem (TouristLeisureCounts,
// OutingPeople — from its Burst jobs, Round S1); SkiGuestCounterSystem and SkiDaySystem (IsShortMonth,
// SimulatedPct, Round M1); tests/ResortBatch MoneyBatch (T1-T7; T9-T11 Round S1; T12 Round M1; T13 Round M2).
//
//   * Lift charge (D23): Full = LiftFixedPerMonth + LiftPerKmPerMonth × km; a lift that is not
//     complete, or complete without power (LiftServedRule.IsPowered, e1-spec R1), or (Round M2 T2,
//     docs/research/round-m2/decisions.md R3) a complete, powered lift whose ski area has no counted run
//     (LiftAreaRunOpen false: the area's released score counts 0 runs), bills LiftReducedShare of that.
//     Phase D: the charge bills through LiftUpkeepBillingOf, so "without power" no longer reduces it (paragraph below).
//     LiftOperating(complete, powered) is the charge's operating count (logs and the hub only; the ticket
//     gate no longer reads lifts since Round M2 T1).
//   * Accrual: the run-upkeep pattern (SkiRunUpkeepSystem's month = TicksPerDay × daysPerYear / 12
//     sim frames; TimeSystem.kTicksPerDay, Game.Simulation/TimeSystem.cs:18): carry += rate × elapsed
//     / month, the whole credits are debited, the fraction carried.
//   * Ticket gate (D26, R6; Round M2 T1, decisions.md R2): Off (price 0) → NoEfficiency (sign efficiency
//     not > 0) → NotKnownYet (no score yet, or the area's first score after a load still held) → Open on a
//     counted run, else NothingOpen (a running lift alone no longer opens it; the operating-lift branch and
//     its parameter are gone). TicketGate's literal values are compared with bindings.ts TICKET_GATE by gate
//     rule R17 (unchanged).
//   * Months: MonthKey = year × 12 + calendar month 0..11; MonthOfKey is 1..12. Round M1 (D5): a month with
//     fewer than FullMonthShare (95 %) of its frames simulated is short (IsShortMonth) and is booked as partial.
//   * Guest days (Round S1 D23, ski-days spec ch3 §1.2): per ski area and game day, a tourist on leisure at a
//     lodge counts once and not when its household came by outing that day; an outing counts only the
//     members not yet counted there that day. The household's day flags are GuestDayOuting | GuestDayLeisure;
//     the day key is SkiDayRules.DayKey (s1-spec N3), the day sets live in the counter.
//
// PURE FILE: `using System;` and `using Unity.Mathematics;` only, + SkiCatalog consts (compile-time literals;
// Burst sees numbers — never a SkiCatalog.Lifts[…] row read here, contract D4 / risk R-c). Every method is static,
// allocation-free and Burst-callable (the ticket job calls TicketGateOf / TicketsPaid).
//
// Created 2026-10-04 (Round E1 stage 0, S0: ch4 §6.1 verbatim); owned by stream S4.
// Round M2 (2026-10-10, builder B3, docs/research/round-m2/build-contract.md): T1 TicketGateOf loses the
// operating-lift branch and parameter; T2 LiftBilling.NoOpenRun, the three-argument LiftBillingOf, LiftAreaRunOpen.
// Catalog Phase A (2026-10-11, docs/research/art/assets-2026-10/catalog-a/phase-a-contract.md R7): the two lift
// charge numbers alias SkiCatalog.FixedGrip (one source; tests/ResortBatch CatalogGuardTests CG-1, MoneyBatch T1-T7
// unchanged). LiftReducedShare stays global policy here (extensibility.md §1).
// Catalog Phase B (2026-10-11, docs/research/art/assets-2026-10/code-round-2/round-2-contract.md §6.2, lift group):
//   * LiftFullRate(lengthM, fixedPerMonth, perKmPerMonth) and LiftRate(billing, lengthM, fixed, perKm): the lift type's
//     rate, the numbers passed in by the caller (SkiLiftChargeSystem reads the row on the main thread; nothing here
//     reads a SkiCatalog row, risk R-a). The one-length overloads stay = the fixed-grip consts (MoneyBatch T1-T7).
//   * Lodge spend (seam S3, D15): LodgeSpendDefault / Max / Step alias SkiCatalog.LodgeSpend (CG-1);
//     ClampLodgeSpend, LodgeSpendOf(setting, multiplier) = floor(setting × multiplier + 0.5) (0 when either is not
//     > 0), SpendPaid(paid, spendPerPerson, moneyAfterTickets) = min(paid, money / spend) (0 when any is not > 0):
//     the ticket job charges what a household has left after its tickets, never into debt (DV9).
// Phase D (2026-10-11, docs/research/art/assets-2026-10/code-round-4/round-4-contract-skeptic.md MUST-1 (a); Richard's
// sub-ruling ~15:25 under DECISIONS 171: "an unpowered lift still costs its maintenance ('it does not magically
// disappear') — upkeep is charged regardless of power"; rules group): LiftUpkeepBillingOf(complete, areaRunOpen) is the
// billing SkiLiftChargeSystem applies — LiftBillingOf with powered = true, so power never reduces the upkeep. The reductions
// that remain (each LiftReducedShare, 25 %): Unfinished (any census state but Complete) and NoOpenRun (complete, its ski area
// counts no run). A run counts only when its value, the snow open factor included, is > 0 (SkiResortScore.cs:326-338,
// :369-380), so NoOpenRun applies in every period without snow at ANY backcountry share, and with backcountry 0 to an area
// whose only serving lift is unpowered. An unpowered complete lift lands there like every complete lift of its area: it pays
// in full while its area counts a run, 25 % while it counts none (round-4-integration.md §6 L1 asks Richard to confirm this
// or choose "strict", i.e. an unpowered lift always in full). LiftOperating and the charge's Operating count keep the real
// power. LiftBillingOf, the LiftBilling enum (NoPower stays bound, now unreachable from the
// charge) and their MoneyBatch oracles are unchanged.

using System;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public static class SkiMoneyRules
    {
        // D23. Catalog Phase A (ext row 13; was SkiMoneyRules.cs:46-47 = 2500f / 1000f): const aliases (D4).
        public const float LiftFixedPerMonth = SkiCatalog.FixedGrip.FixedPerMonth;
        public const float LiftPerKmPerMonth = SkiCatalog.FixedGrip.PerKmPerMonth;
        public const float LiftReducedShare = 0.25f;
        // D26
        public const int TicketPriceDefault = 40;
        public const int TicketPriceMax = 200;
        public const int TicketPriceStep = 5;
        // time (TimeSystem.kTicksPerDay, Game.Simulation/TimeSystem.cs:18; the ECS systems assert it at arm time)
        public const int TicksPerDay = 262144;
        public const int MonthsPerYear = 12;

        // Round M2 T2 appends NoOpenRun = 3 (not bound to the UI: no R17 entry).
        public enum LiftBilling : byte { Full = 0, NoPower = 1, Unfinished = 2, NoOpenRun = 3 }
        public enum TicketGate : byte { Open = 0, Off = 1, NoEfficiency = 2, NothingOpen = 3, NotKnownYet = 4 }

        public static bool LiftOperating(bool complete, bool powered) => complete && powered;

        /// <summary>Round M2 T2 (decisions.md R3): precedence unfinished, then no power, then no counted run in
        /// the lift's ski area (areaRunOpen from LiftAreaRunOpen). Any billing but Full bills LiftReducedShare.</summary>
        public static LiftBilling LiftBillingOf(bool complete, bool powered, bool areaRunOpen)
            => !complete ? LiftBilling.Unfinished : !powered ? LiftBilling.NoPower
               : areaRunOpen ? LiftBilling.Full : LiftBilling.NoOpenRun;

        /// <summary>Phase D (skeptic MUST-1 (a); DECISIONS 171 sub-ruling): THE billing of the lift charge — power never
        /// reduces the upkeep, so this is LiftBillingOf(complete, powered: true, areaRunOpen): Unfinished, else NoOpenRun when
        /// the area has no counted run, else Full. It takes no power argument on purpose.</summary>
        public static LiftBilling LiftUpkeepBillingOf(bool complete, bool areaRunOpen)
            => LiftBillingOf(complete, true, areaRunOpen);

        /// <summary>Round M2 T2: the lift's ski area has a counted run, for billing. Unknown reads true (full, the
        /// old rate): not linked, no score yet, or the area's first score after a load still held.</summary>
        public static bool LiftAreaRunOpen(bool linked, bool scoreKnown, bool writeHeld, int runsCounted)
            => !linked || !scoreKnown || writeHeld || runsCounted > 0;

        /// <summary>2500 + 1000 × km; a negative or non-finite length counts as 0 m. Catalog Phase B: = the fixed-grip
        /// row's LiftFullRate(lengthM, LiftFixedPerMonth, LiftPerKmPerMonth).</summary>
        public static float LiftFullRate(float lengthM)
            => LiftFullRate(lengthM, LiftFixedPerMonth, LiftPerKmPerMonth);

        public static float LiftRate(LiftBilling billing, float lengthM)
            => LiftRate(billing, lengthM, LiftFixedPerMonth, LiftPerKmPerMonth);

        /// <summary>Catalog Phase B (round-2-contract §6.2): the lift type's full rate, fixedPerMonth + perKmPerMonth ×
        /// km (LiftTypeDef.FixedPerMonth / PerKmPerMonth, passed in by the caller); a negative or non-finite length counts
        /// as 0 m, a negative or non-finite rate part as 0.</summary>
        public static float LiftFullRate(float lengthM, float fixedPerMonth, float perKmPerMonth)
        {
            float m = math.isfinite(lengthM) && lengthM > 0f ? lengthM : 0f;
            float fixedPart = math.isfinite(fixedPerMonth) && fixedPerMonth > 0f ? fixedPerMonth : 0f;
            float perKm = math.isfinite(perKmPerMonth) && perKmPerMonth > 0f ? perKmPerMonth : 0f;
            return fixedPart + perKm * m / 1000f;
        }

        /// <summary>Catalog Phase B: LiftRate with the lift type's two rate numbers; any billing but Full bills
        /// LiftReducedShare (global policy) of the full rate.</summary>
        public static float LiftRate(LiftBilling billing, float lengthM, float fixedPerMonth, float perKmPerMonth)
            => billing == LiftBilling.Full ? LiftFullRate(lengthM, fixedPerMonth, perKmPerMonth)
                                           : LiftReducedShare * LiftFullRate(lengthM, fixedPerMonth, perKmPerMonth);

        // ── Catalog Phase B seam S3 (round-2-contract §6.2, D15): lodge spend per paying guest ─────────────────────
        public const int LodgeSpendDefault = SkiCatalog.LodgeSpend.Default;   // 0 = off (ResortBatch CG-1)
        public const int LodgeSpendMax = SkiCatalog.LodgeSpend.Max;           // 50
        public const int LodgeSpendStep = SkiCatalog.LodgeSpend.Step;         // 5

        /// <summary>The slider value clamped to 0..LodgeSpendMax (the settings file can hold anything).</summary>
        public static int ClampLodgeSpend(int v) => math.clamp(v, 0, LodgeSpendMax);

        /// <summary>Spend per person at one lodge: floor(setting × multiplier + 0.5) (LodgeDef.SpendMultiplier: A-frame
        /// and hut 1, chalet 1.5, summit and grand 3); 0 when the setting is not &gt; 0 or the multiplier not &gt; 0.</summary>
        public static int LodgeSpendOf(int setting, float multiplier)
            => setting <= 0 || !(multiplier > 0f) ? 0 : (int)math.floor(setting * multiplier + 0.5f);

        /// <summary>Paying guests of a household that also spend at the lodge: min(paid, money left after the tickets /
        /// spend per person) — never into debt; 0 when any input is not &gt; 0.</summary>
        public static int SpendPaid(int paidPeople, int spendPerPerson, int moneyAfterTickets)
            => paidPeople <= 0 || spendPerPerson <= 0 || moneyAfterTickets <= 0
                ? 0
                : math.min(paidPeople, moneyAfterTickets / spendPerPerson);

        /// <summary>Sim frames in one month: TicksPerDay × max(1, daysPerYear) / 12 (SkiRunUpkeepSystem's month).</summary>
        public static float MonthFrames(int daysPerYear) => (float)TicksPerDay * math.max(1, daysPerYear) / MonthsPerYear;

        // Round M1 (D5, m1-spec M7c): a developer time jump moves the clock, not the frame counter, so a jump that
        // lands exactly one month later rolls a month that was barely simulated. Such a month is partial.
        /// <summary>A month counts as full from this share of its frames simulated on (one "+1 h" press keeps a
        /// month full at 95.8 %, two make it partial; m1-spec Open point 2).</summary>
        public const float FullMonthShare = 0.95f;

        /// <summary>Fewer than FullMonthShare × MonthFrames(daysPerYear) frames simulated (double compare, so a
        /// large frame count loses no precision).</summary>
        public static bool IsShortMonth(uint framesSimulated, int daysPerYear)
            => (double)framesSimulated < (double)FullMonthShare * MonthFrames(daysPerYear);

        /// <summary>The share of a month simulated in whole per cent: min(100, floor(100 × frames / MonthFrames)).</summary>
        public static int SimulatedPct(uint framesSimulated, int daysPerYear)
            => (int)math.min(100.0, math.floor(100.0 * framesSimulated / MonthFrames(daysPerYear)));

        /// <summary>The upkeep's accrual: carry += rate × elapsed / month; returns the whole credits to debit
        /// and keeps the fraction. A rate that is not > 0 accrues nothing.</summary>
        public static int Accrue(ref float carry, float monthlyRate, uint elapsedFrames, int daysPerYear)
        {
            if (!(monthlyRate > 0f)) return 0;
            carry += monthlyRate * (elapsedFrames / MonthFrames(daysPerYear));
            int debit = (int)carry;
            carry -= debit;
            return debit;
        }

        /// <summary>What a rate accrues over elapsedFrames (for per-ski-area attribution).</summary>
        public static double AccruedShare(float monthlyRate, uint elapsedFrames, int daysPerYear)
            => monthlyRate > 0f ? (double)monthlyRate * elapsedFrames / MonthFrames(daysPerYear) : 0.0;

        public static int ClampTicketPrice(int price) => math.clamp(price, 0, TicketPriceMax);

        /// <summary>Round M2 T1 (decisions.md R2): tickets need a counted open run. runStateKnown = a score is
        /// present AND not in the first-write hold (callers: the ticket job, the gate watch, the hub). Free entry
        /// during a load hold is accepted (at most RatingHoldMaxTicks = 1,024 ticks = 16,384 frames).</summary>
        public static TicketGate TicketGateOf(int price, float efficiency, bool runStateKnown, int openRuns)
        {
            if (price <= 0) return TicketGate.Off;
            if (!(efficiency > 0f)) return TicketGate.NoEfficiency;
            if (!runStateKnown) return TicketGate.NotKnownYet;
            return openRuns > 0 ? TicketGate.Open : TicketGate.NothingOpen;
        }

        /// <summary>Whole tickets a household pays: min(people, money / price); 0 when any input is not > 0.</summary>
        public static int TicketsPaid(int people, int price, int householdMoney)
        {
            if (people <= 0 || price <= 0 || householdMoney <= 0) return 0;
            return math.min(people, householdMoney / price);
        }

        /// <summary>year × 12 + calendar month 0..11 of the game year; a non-finite date reads month 0.</summary>
        public static int MonthKey(int year, float normalizedDate)
        {
            int m = math.isfinite(normalizedDate) ? (int)math.floor(normalizedDate * MonthsPerYear) : 0;
            return year * MonthsPerYear + math.clamp(m, 0, MonthsPerYear - 1);
        }

        public static int YearOfKey(int key) => (int)math.floor(key / (double)MonthsPerYear);
        public static int MonthOfKey(int key) => key - YearOfKey(key) * MonthsPerYear + 1;   // 1..12

        // Round S1 D23 (spec ski-days ch3 §1): per ski area, per game day.
        public const byte GuestDayOuting = 1;    // household counted there today through an attraction outing
        public const byte GuestDayLeisure = 2;   // household counted there today through a member's leisure visit
        // (no DayKey here: the counter calls SkiDayRules.DayKey, s1-spec N3)

        /// <summary>A tourist's leisure arrival at a lodge counts unless the citizen was counted at this ski area
        /// today or its household came by outing today. newGroup: the household's first count there today.</summary>
        public static bool TouristLeisureCounts(bool citizenCountedToday, byte householdDay, out bool newGroup)
        {
            newGroup = false;
            if (citizenCountedToday || (householdDay & GuestDayOuting) != 0)
                return false;
            newGroup = householdDay == 0;
            return true;
        }

        /// <summary>An outing that reached Attending: the attendees not yet counted at this ski area today
        /// (alreadyCountedToday clamped to 0..party). newGroup when someone is counted and the household had no
        /// count there today.</summary>
        public static int OutingPeople(int partySize, int alreadyCountedToday, byte householdDay, out bool newGroup)
        {
            int party = math.max(0, partySize);
            int people = party - math.clamp(alreadyCountedToday, 0, party);
            newGroup = people > 0 && householdDay == 0;
            return people;
        }
    }
}
