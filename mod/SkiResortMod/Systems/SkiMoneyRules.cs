// SkiMoneyRules — the PURE money rules of Round E1 (docs/research/econ-round/spec/ch4-money.md §6.1;
// decisions D23 lift running charge, D26 lift tickets). Consumers: SkiLiftChargeSystem (LiftBillingOf,
// LiftRate, Accrue, AccruedShare), SkiLiftTicketSystem (ClampTicketPrice, TicketGateOf, TicketsPaid —
// from its Burst job), SkiGuestCounterSystem (MonthKey), SkiResortStatsSystem / HubRules (gate, price,
// item rates), the settings (TicketPriceDefault); tests/ResortBatch MoneyBatch (T1-T7).
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
//   * Months: MonthKey = year × 12 + calendar month 0..11; MonthOfKey is 1..12.
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
    }
}
