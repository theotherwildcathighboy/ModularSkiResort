// SkiMoneyRules — the PURE money rules of Round E1 (docs/research/econ-round/spec/ch4-money.md §6.1;
// decisions D23 lift running charge, D26 lift tickets). Consumers: SkiLiftChargeSystem (LiftBillingOf,
// LiftRate, Accrue, AccruedShare), SkiLiftTicketSystem (ClampTicketPrice, TicketGateOf, TicketsPaid —
// from its Burst job), SkiGuestCounterSystem (MonthKey), SkiResortStatsSystem / HubRules (gate, price,
// item rates), the settings (TicketPriceDefault); SkiGuestCounterSystem (TouristLeisureCounts, OutingPeople —
// from its Burst jobs, Round S1); SkiGuestCounterSystem and SkiDaySystem (IsShortMonth, SimulatedPct, Round M1);
// tests/ResortBatch MoneyBatch (T1-T7; T9-T11 Round S1; T12 Round M1).
//
//   * Lift charge (D23): Full = LiftFixedPerMonth + LiftPerKmPerMonth × km; a lift that is not
//     complete, or complete without power (LiftServedRule.IsPowered, e1-spec R1), bills
//     LiftReducedShare of that. LiftOperating(complete, powered) feeds the ticket gate (R6).
//   * Accrual: the run-upkeep pattern (SkiRunUpkeepSystem's month = TicksPerDay × daysPerYear / 12
//     sim frames; TimeSystem.kTicksPerDay, Game.Simulation/TimeSystem.cs:18): carry += rate × elapsed
//     / month, the whole credits are debited, the fraction carried.
//   * Ticket gate (D26, R6): Off (price 0) → NoEfficiency (sign efficiency not > 0) → Open on an
//     operating lift → NotKnownYet (no score result yet, DV10) → Open on a counted run, else NothingOpen.
//     TicketGate's literal values are compared with bindings.ts TICKET_GATE by gate rule R17.
//   * Months: MonthKey = year × 12 + calendar month 0..11; MonthOfKey is 1..12. Round M1 (D5): a month with
//     fewer than FullMonthShare (95 %) of its frames simulated is short (IsShortMonth) and is booked as partial.
//   * Guest days (Round S1 D23, ski-days spec ch3 §1.2): per ski area and game day, a tourist on leisure at a
//     lodge counts once and not when its household came by outing that day; an outing counts only the
//     members not yet counted there that day. The household's day flags are GuestDayOuting | GuestDayLeisure;
//     the day key is SkiDayRules.DayKey (s1-spec N3), the day sets live in the counter.
//
// PURE FILE: `using System;` and `using Unity.Mathematics;` only. Every method is static,
// allocation-free and Burst-callable (the ticket job calls TicketGateOf / TicketsPaid).
//
// Created 2026-10-04 (Round E1 stage 0, S0: ch4 §6.1 verbatim); owned by stream S4.

using System;
using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public static class SkiMoneyRules
    {
        // D23
        public const float LiftFixedPerMonth = 2500f;
        public const float LiftPerKmPerMonth = 1000f;
        public const float LiftReducedShare = 0.25f;
        // D26
        public const int TicketPriceDefault = 40;
        public const int TicketPriceMax = 200;
        public const int TicketPriceStep = 5;
        // time (TimeSystem.kTicksPerDay, Game.Simulation/TimeSystem.cs:18; the ECS systems assert it at arm time)
        public const int TicksPerDay = 262144;
        public const int MonthsPerYear = 12;

        public enum LiftBilling : byte { Full = 0, NoPower = 1, Unfinished = 2 }
        public enum TicketGate : byte { Open = 0, Off = 1, NoEfficiency = 2, NothingOpen = 3, NotKnownYet = 4 }

        public static bool LiftOperating(bool complete, bool powered) => complete && powered;

        public static LiftBilling LiftBillingOf(bool complete, bool powered)
            => !complete ? LiftBilling.Unfinished : powered ? LiftBilling.Full : LiftBilling.NoPower;

        /// <summary>2500 + 1000 × km; a negative or non-finite length counts as 0 m.</summary>
        public static float LiftFullRate(float lengthM)
        {
            float m = math.isfinite(lengthM) && lengthM > 0f ? lengthM : 0f;
            return LiftFixedPerMonth + LiftPerKmPerMonth * m / 1000f;
        }

        public static float LiftRate(LiftBilling billing, float lengthM)
            => billing == LiftBilling.Full ? LiftFullRate(lengthM) : LiftReducedShare * LiftFullRate(lengthM);

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

        public static TicketGate TicketGateOf(int price, float efficiency, bool runStateKnown, int openRuns, int operatingLifts)
        {
            if (price <= 0) return TicketGate.Off;
            if (!(efficiency > 0f)) return TicketGate.NoEfficiency;
            if (operatingLifts > 0) return TicketGate.Open;
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
