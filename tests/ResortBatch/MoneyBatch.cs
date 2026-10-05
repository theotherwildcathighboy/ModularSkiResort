// MoneyBatch — Round E1 test file for tests/ResortBatch (econ-round spec ch4 §7: money rules and ledger, T1-T7).
// Owner: stream S4 (docs/research/econ-round/e1-spec.md §3.2). Created empty by stage 0 (S0, 2026-10-04):
// e1-spec R9 — `public static class MoneyBatch` with `public static void Run(Func<bool, string, string, bool> check)`
// and its own Near; Program.Main calls MoneyBatch.Run(Check). Oracles are literal hand values (ch4 §7).
// Stream S4 (2026-10-04) wrote T1-T7; T8 (SkiResortMoney.Compose) is dropped with Compose (e1-spec R7).
//
// Covers: SkiMoneyRules (T1 constants, T2 lift rates and billing, T3 accrual and attribution, T4 ticket price
// and gate, T5 affordability, T6 month keys) and SkiResortLedger (T7: start / roll / jump, the per-entry
// counters, Revision, Prune, Reset). Not testable headless (ECS): the scans, the ticket job, the lift
// snapshot — covered by the logs, the DUMP and the field rows of ch4 §15.

using System;
using System.Collections.Generic;
using SkiResortMod.Systems;
using Billing = SkiResortMod.Systems.SkiMoneyRules.LiftBilling;
using Gate = SkiResortMod.Systems.SkiMoneyRules.TicketGate;
using Advance = SkiResortMod.Systems.SkiResortLedger.AdvanceResult;

namespace ResortBatch
{
    public static class MoneyBatch
    {
        public static void Run(Func<bool, string, string, bool> check)
        {
            Constants(check);
            LiftRates(check);
            Accrual(check);
            PriceAndGate(check);
            Affordability(check);
            MonthKeys(check);
            Ledger(check);
        }

        private static bool Near(double a, double b, double eps)
        {
            return Math.Abs(a - b) <= eps;
        }

        // ── T1 constants ────────────────────────────────────────────────────────────────────────
        private static void Constants(Func<bool, string, string, bool> check)
        {
            const string w = "money T1";
            check(SkiMoneyRules.LiftFixedPerMonth == 2500f, w, "LiftFixedPerMonth " + SkiMoneyRules.LiftFixedPerMonth);
            check(SkiMoneyRules.LiftPerKmPerMonth == 1000f, w, "LiftPerKmPerMonth " + SkiMoneyRules.LiftPerKmPerMonth);
            check(SkiMoneyRules.LiftReducedShare == 0.25f, w, "LiftReducedShare " + SkiMoneyRules.LiftReducedShare);
            check(SkiMoneyRules.TicketPriceDefault == 40, w, "TicketPriceDefault " + SkiMoneyRules.TicketPriceDefault);
            check(SkiMoneyRules.TicketPriceMax == 200, w, "TicketPriceMax " + SkiMoneyRules.TicketPriceMax);
            check(SkiMoneyRules.TicketPriceStep == 5, w, "TicketPriceStep " + SkiMoneyRules.TicketPriceStep);
            check(SkiMoneyRules.TicksPerDay == 262144, w, "TicksPerDay " + SkiMoneyRules.TicksPerDay);
        }

        // ── T2 lift rates ───────────────────────────────────────────────────────────────────────
        private static void LiftRates(Func<bool, string, string, bool> check)
        {
            const string w = "money T2";
            check(SkiMoneyRules.LiftFullRate(1000f) == 3500f, w, "LiftFullRate(1000) " + SkiMoneyRules.LiftFullRate(1000f));
            check(SkiMoneyRules.LiftFullRate(991f) == 3491f, w, "LiftFullRate(991) " + SkiMoneyRules.LiftFullRate(991f));
            check(SkiMoneyRules.LiftFullRate(2281f) == 4781f, w, "LiftFullRate(2281) " + SkiMoneyRules.LiftFullRate(2281f));
            check(SkiMoneyRules.LiftFullRate(0f) == 2500f, w, "LiftFullRate(0) " + SkiMoneyRules.LiftFullRate(0f));
            check(SkiMoneyRules.LiftFullRate(-5f) == 2500f, w, "LiftFullRate(-5) " + SkiMoneyRules.LiftFullRate(-5f));
            check(SkiMoneyRules.LiftFullRate(float.NaN) == 2500f, w, "LiftFullRate(NaN) " + SkiMoneyRules.LiftFullRate(float.NaN));
            check(SkiMoneyRules.LiftFullRate(float.PositiveInfinity) == 2500f, w,
                  "LiftFullRate(+inf) " + SkiMoneyRules.LiftFullRate(float.PositiveInfinity));

            check(SkiMoneyRules.LiftBillingOf(true, true) == Billing.Full, w, "LiftBillingOf(complete, powered)");
            check(SkiMoneyRules.LiftBillingOf(true, false) == Billing.NoPower, w, "LiftBillingOf(complete, no power)");
            check(SkiMoneyRules.LiftBillingOf(false, true) == Billing.Unfinished, w, "LiftBillingOf(open, powered)");
            check(SkiMoneyRules.LiftBillingOf(false, false) == Billing.Unfinished, w, "LiftBillingOf(open, no power)");

            check(SkiMoneyRules.LiftRate(Billing.Full, 1000f) == 3500f, w, "LiftRate(Full, 1000) " + SkiMoneyRules.LiftRate(Billing.Full, 1000f));
            check(SkiMoneyRules.LiftRate(Billing.NoPower, 1000f) == 875f, w, "LiftRate(NoPower, 1000) " + SkiMoneyRules.LiftRate(Billing.NoPower, 1000f));
            check(SkiMoneyRules.LiftRate(Billing.Unfinished, 0f) == 625f, w, "LiftRate(Unfinished, 0) " + SkiMoneyRules.LiftRate(Billing.Unfinished, 0f));
            check(SkiMoneyRules.LiftRate(Billing.Unfinished, 991f) == 872.75f, w,
                  "LiftRate(Unfinished, 991) " + SkiMoneyRules.LiftRate(Billing.Unfinished, 991f));
            check(SkiMoneyRules.LiftRate(Billing.NoPower, 2281f) == 1195.25f, w,
                  "LiftRate(NoPower, 2281) " + SkiMoneyRules.LiftRate(Billing.NoPower, 2281f));

            check(SkiMoneyRules.LiftOperating(true, true), w, "LiftOperating(complete, powered)");
            check(!SkiMoneyRules.LiftOperating(true, false), w, "!LiftOperating(complete, no power)");
            check(!SkiMoneyRules.LiftOperating(false, true), w, "!LiftOperating(open, powered)");
        }

        // ── T3 accrual ──────────────────────────────────────────────────────────────────────────
        private static void Accrual(Func<bool, string, string, bool> check)
        {
            const string w = "money T3";
            check(SkiMoneyRules.MonthFrames(12) == 262144f, w, "MonthFrames(12) " + SkiMoneyRules.MonthFrames(12));
            check(SkiMoneyRules.MonthFrames(24) == 524288f, w, "MonthFrames(24) " + SkiMoneyRules.MonthFrames(24));
            check(Near(SkiMoneyRules.MonthFrames(0), 21845.334, 0.001), w, "MonthFrames(0) " + SkiMoneyRules.MonthFrames(0));
            check(SkiMoneyRules.MonthFrames(1) == SkiMoneyRules.MonthFrames(0), w, "MonthFrames(1) == MonthFrames(0)");

            float carry = 0f;
            int d1 = SkiMoneyRules.Accrue(ref carry, 3500f, 1024, 12);
            check(d1 == 13, w, "Accrue #1 debit " + d1);
            check(carry == 0.671875f, w, "Accrue #1 carry " + carry.ToString("R"));
            int d2 = SkiMoneyRules.Accrue(ref carry, 3500f, 1024, 12);
            check(d2 == 14, w, "Accrue #2 debit " + d2);
            check(carry == 0.34375f, w, "Accrue #2 carry " + carry.ToString("R"));
            long total = d1 + d2;
            for (int i = 0; i < 254; i++)
                total += SkiMoneyRules.Accrue(ref carry, 3500f, 1024, 12);
            check(total == 3500, w, "Accrue 256 passes = one month: total " + total);
            check(carry == 0f, w, "Accrue 256 passes: final carry " + carry.ToString("R"));

            float c = 0.25f;
            check(SkiMoneyRules.Accrue(ref c, 0f, 1024, 12) == 0 && c == 0.25f, w, "Accrue(rate 0) debits 0, carry unchanged " + c.ToString("R"));
            check(SkiMoneyRules.Accrue(ref c, -10f, 1024, 12) == 0 && c == 0.25f, w, "Accrue(rate -10) debits 0, carry unchanged " + c.ToString("R"));
            check(SkiMoneyRules.Accrue(ref c, float.NaN, 1024, 12) == 0 && c == 0.25f, w, "Accrue(rate NaN) debits 0, carry unchanged " + c.ToString("R"));

            check(SkiMoneyRules.AccruedShare(875f, 1024, 12) == 3.41796875, w, "AccruedShare(875) " + SkiMoneyRules.AccruedShare(875f, 1024, 12).ToString("R"));
            check(SkiMoneyRules.AccruedShare(3500f, 1024, 12) == 13.671875, w, "AccruedShare(3500) " + SkiMoneyRules.AccruedShare(3500f, 1024, 12).ToString("R"));
            check(SkiMoneyRules.AccruedShare(0f, 1024, 12) == 0.0, w, "AccruedShare(0) " + SkiMoneyRules.AccruedShare(0f, 1024, 12).ToString("R"));
            check(SkiMoneyRules.AccruedShare(float.NaN, 1024, 12) == 0.0, w, "AccruedShare(NaN) " + SkiMoneyRules.AccruedShare(float.NaN, 1024, 12).ToString("R"));
            double parts = SkiMoneyRules.AccruedShare(3491f, 1024, 12) + SkiMoneyRules.AccruedShare(1195.25f, 1024, 12);
            double whole = SkiMoneyRules.AccruedShare(4686.25f, 1024, 12);
            check(Near(parts, whole, 1e-9), w, "attribution: shares sum to the total share: " + parts.ToString("R") + " vs " + whole.ToString("R"));
            check(Near(whole, 18.3056640625, 1e-9), w, "AccruedShare(4686.25) " + whole.ToString("R"));
        }

        // ── T4 ticket price and gate ────────────────────────────────────────────────────────────
        private static void PriceAndGate(Func<bool, string, string, bool> check)
        {
            const string w = "money T4";
            check(SkiMoneyRules.ClampTicketPrice(-5) == 0, w, "ClampTicketPrice(-5)");
            check(SkiMoneyRules.ClampTicketPrice(0) == 0, w, "ClampTicketPrice(0)");
            check(SkiMoneyRules.ClampTicketPrice(40) == 40, w, "ClampTicketPrice(40)");
            check(SkiMoneyRules.ClampTicketPrice(200) == 200, w, "ClampTicketPrice(200)");
            check(SkiMoneyRules.ClampTicketPrice(999) == 200, w, "ClampTicketPrice(999)");

            GateCase(check, w, 0, 1f, true, 3, 1, Gate.Off);
            GateCase(check, w, 0, 0f, false, 0, 0, Gate.Off);                  // price first
            GateCase(check, w, 40, 0f, true, 3, 1, Gate.NoEfficiency);
            GateCase(check, w, 40, float.NaN, true, 3, 1, Gate.NoEfficiency);
            GateCase(check, w, 40, 0.01f, true, 0, 0, Gate.NothingOpen);
            GateCase(check, w, 40, 1f, false, 0, 0, Gate.NotKnownYet);
            GateCase(check, w, 40, 1f, false, 0, 1, Gate.Open);
            GateCase(check, w, 40, 0.04f, true, 1, 0, Gate.Open);
            GateCase(check, w, 40, 1.25f, true, 0, 2, Gate.Open);

            // Literal values (gate rule R17 compares them with bindings.ts TICKET_GATE).
            check((int)Gate.Open == 0 && (int)Gate.Off == 1 && (int)Gate.NoEfficiency == 2 &&
                  (int)Gate.NothingOpen == 3 && (int)Gate.NotKnownYet == 4, w, "TicketGate literal values 0-4");
        }

        private static void GateCase(Func<bool, string, string, bool> check, string w, int price, float efficiency,
                                     bool known, int openRuns, int operating, Gate expected)
        {
            Gate g = SkiMoneyRules.TicketGateOf(price, efficiency, known, openRuns, operating);
            check(g == expected, w, $"TicketGateOf({price}, {efficiency}, {known}, {openRuns}, {operating}) = {g}, expected {expected}");
        }

        // ── T5 affordability ────────────────────────────────────────────────────────────────────
        private static void Affordability(Func<bool, string, string, bool> check)
        {
            const string w = "money T5";
            PaidCase(check, w, 3, 40, 1000, 3);
            PaidCase(check, w, 3, 40, 100, 2);
            PaidCase(check, w, 3, 40, 39, 0);
            PaidCase(check, w, 3, 40, -500, 0);
            PaidCase(check, w, 2, 0, 1000, 0);
            PaidCase(check, w, 1, 40, 40, 1);
            PaidCase(check, w, 5, 200, 999, 4);
            PaidCase(check, w, 0, 40, 1000, 0);
            PaidCase(check, w, 100, 200, int.MaxValue, 100);
            // Never into debt: whatever is paid is covered by the household's money.
            bool covered = true;
            for (int money = -10; money <= 1000; money += 7)
            {
                for (int people = 0; people <= 6; people++)
                {
                    int paid = SkiMoneyRules.TicketsPaid(people, 40, money);
                    if (paid < 0 || paid > people || (paid > 0 && (long)paid * 40 > money))
                        covered = false;
                }
            }
            check(covered, w, "TicketsPaid never charges more than the household holds (people 0-6, money -10..1000, price 40)");
        }

        private static void PaidCase(Func<bool, string, string, bool> check, string w, int people, int price, int money, int expected)
        {
            int paid = SkiMoneyRules.TicketsPaid(people, price, money);
            check(paid == expected, w, $"TicketsPaid({people}, {price}, {money}) = {paid}, expected {expected}");
        }

        // ── T6 month key ────────────────────────────────────────────────────────────────────────
        private static void MonthKeys(Func<bool, string, string, bool> check)
        {
            const string w = "money T6";
            KeyCase(check, w, 1, 0f, 12);
            KeyCase(check, w, 1, 0.5f, 18);
            KeyCase(check, w, 1, 0.9999f, 23);
            KeyCase(check, w, 1, 1f, 23);
            KeyCase(check, w, 2, 0f, 24);
            KeyCase(check, w, 3, 0.0834f, 37);
            KeyCase(check, w, 3, float.NaN, 36);
            check(SkiMoneyRules.YearOfKey(37) == 3 && SkiMoneyRules.MonthOfKey(37) == 2, w,
                  $"key 37 = year {SkiMoneyRules.YearOfKey(37)} month {SkiMoneyRules.MonthOfKey(37)}");
            check(SkiMoneyRules.YearOfKey(23) == 1 && SkiMoneyRules.MonthOfKey(23) == 12, w,
                  $"key 23 = year {SkiMoneyRules.YearOfKey(23)} month {SkiMoneyRules.MonthOfKey(23)}");
            check(SkiMoneyRules.YearOfKey(24) == 2 && SkiMoneyRules.MonthOfKey(24) == 1, w,
                  $"key 24 = year {SkiMoneyRules.YearOfKey(24)} month {SkiMoneyRules.MonthOfKey(24)}");
        }

        private static void KeyCase(Func<bool, string, string, bool> check, string w, int year, float date, int expected)
        {
            int key = SkiMoneyRules.MonthKey(year, date);
            check(key == expected, w, $"MonthKey({year}, {date}) = {key}, expected {expected}");
        }

        // ── T7 ledger ───────────────────────────────────────────────────────────────────────────
        private static void Ledger(Func<bool, string, string, bool> check)
        {
            const string w = "money T7";
            var a = new EntityRef(412, 1);
            var b = new EntityRef(500, 2);
            var n = EntityRef.Null;
            var l = new SkiResortLedger();

            // 1. new ledger
            check(l.MonthKey == SkiResortLedger.NoMonth, w, "1: new ledger MonthKey == NoMonth");
            check(l.Count == 0, w, "1: new ledger Count == 0");
            check(!l.LastMonthKnown, w, "1: new ledger !LastMonthKnown");

            // 2. start
            int rev0 = l.Revision;
            check(l.Advance(100) == Advance.Started, w, "2: Advance(100) == Started");
            check(l.ThisMonthSinceLoad, w, "2: ThisMonthSinceLoad");
            check(!l.LastMonthKnown, w, "2: !LastMonthKnown");
            int rev2 = l.Revision;

            // 3. arrivals
            l.AddArrival(a, true, 3);
            int rev3a = l.Revision;
            l.AddArrival(a, false, 1);
            l.AddArrival(a, false, 1);
            int rev3 = l.Revision;
            check(l.TryGet(a, out SkiResortLedger.Entry ea), w, "3: TryGet(A)");
            check(ea.This.TouristGuests == 3, w, "3: A.This.TouristGuests " + ea.This.TouristGuests);
            check(ea.This.TouristParties == 1, w, "3: A.This.TouristParties " + ea.This.TouristParties);
            check(ea.This.LocalGuests == 2, w, "3: A.This.LocalGuests " + ea.This.LocalGuests);
            check(ea.This.Guests == 5, w, "3: A.This.Guests " + ea.This.Guests);

            // 4. tickets, lift charges, a second ski area
            l.AddTickets(a, 4, 1, 0, 160);
            int rev4a = l.Revision;
            l.AddLiftCharge(a, 13.671875);
            l.AddLiftCharge(n, 3.41796875);
            l.AddArrival(b, true, 2);
            int rev4 = l.Revision;
            check(l.Count == 3, w, "4: Count == 3 (A, Not linked, B), got " + l.Count);

            // 5. Revision
            check(rev0 < rev2 && rev2 < rev3a && rev3a < rev3 && rev3 < rev4a && rev4a < rev4, w,
                  $"5: Revision strictly increases {rev0} < {rev2} < {rev3a} < {rev3} < {rev4a} < {rev4}");
            l.AddTickets(a, 0, 0, 0, 0);
            check(l.Revision == rev4, w, "5: AddTickets(A, 0, 0, 0, 0) leaves Revision unchanged");
            l.AddArrival(a, true, -3);
            check(l.Revision == rev4, w, "5: AddArrival(A, true, -3) leaves Revision unchanged");
            l.AddTickets(a, -1, -2, -3, -40);
            l.AddLiftCharge(a, -5.0);
            l.AddLiftCharge(a, double.NaN);
            l.AddLiftCharge(a, 0.0);
            check(l.Revision == rev4, w, "5: negative / NaN / zero amounts leave Revision unchanged");
            l.TryGet(a, out ea);
            check(ea.This.TicketsPaid == 4 && ea.This.TicketIncome == 160 && ea.This.LiftCharged == 13.671875, w,
                  "5: negative amounts never subtract");

            // 6. same month
            check(l.Advance(100) == Advance.None, w, "6: Advance(100) == None");
            check(l.Revision == rev4, w, "6: Advance None leaves Revision unchanged");

            // 7. roll
            check(l.Advance(101) == Advance.Rolled, w, "7: Advance(101) == Rolled");
            l.TryGet(a, out ea);
            SkiResortLedger.Month al = ea.Last;
            check(al.TouristGuests == 3 && al.TouristParties == 1 && al.LocalGuests == 2, w,
                  $"7: A.Last guests {al.TouristGuests}/{al.TouristParties}/{al.LocalGuests}");
            check(al.TicketsPaid == 4 && al.TicketsUnpaid == 1 && al.TicketsFree == 0, w,
                  $"7: A.Last tickets {al.TicketsPaid}/{al.TicketsUnpaid}/{al.TicketsFree}");
            check(al.TicketIncome == 160, w, "7: A.Last.TicketIncome " + al.TicketIncome);
            check(al.LiftCharged == 13.671875, w, "7: A.Last.LiftCharged " + al.LiftCharged.ToString("R"));
            check(IsZero(ea.This), w, "7: A.This all zero");
            check(l.LastMonthKnown, w, "7: LastMonthKnown");
            check(l.LastMonthSinceLoad, w, "7: LastMonthSinceLoad");
            check(!l.ThisMonthSinceLoad, w, "7: !ThisMonthSinceLoad");
            check(l.TryGet(n, out SkiResortLedger.Entry en) && en.Last.LiftCharged == 3.41796875, w,
                  "7: N.Last.LiftCharged " + en.Last.LiftCharged.ToString("R"));
            check(l.MonthKey == 101, w, "7: MonthKey 101");

            // 8. totals
            SkiResortLedger.Month t = l.Total(true);
            check(t.Guests == 7, w, "8: Total(last).Guests " + t.Guests);
            check(t.TicketIncome == 160, w, "8: Total(last).TicketIncome " + t.TicketIncome);
            check(t.LiftCharged == 17.08984375, w, "8: Total(last).LiftCharged " + t.LiftCharged.ToString("R"));
            check(IsZero(l.Total(false)), w, "8: Total(this) all zero");

            // 9. roll again (the first full month)
            check(l.Advance(102) == Advance.Rolled, w, "9: Advance(102) == Rolled");
            check(!l.LastMonthSinceLoad, w, "9: !LastMonthSinceLoad");
            check(l.LastMonthKnown, w, "9: LastMonthKnown");
            l.TryGet(a, out ea);
            check(IsZero(ea.Last), w, "9: A.Last all zero");

            // 10. jump
            l.AddArrival(a, false, 1);
            check(l.Advance(105) == Advance.Jumped, w, "10: Advance(105) == Jumped");
            check(!l.LastMonthKnown, w, "10: !LastMonthKnown");
            // E1 fixer (e1-spec §8.9 F8): a jump restarts counting partway through the month, as a load does.
            check(l.ThisMonthSinceLoad, w, "10: ThisMonthSinceLoad (counting restarted at the jump)");
            bool allZero = true;
            for (int i = 0; i < l.Count; i++)
                allZero &= IsZero(l.At(i).This) && IsZero(l.At(i).Last);
            check(allZero, w, "10: every Last and This zero");
            check(l.Advance(104) == Advance.Jumped, w, "10: a key that goes back is a jump");
            // 10b. the roll after a jump: that month was counted only from the jump, never a full month (+3).
            check(l.Advance(105) == Advance.Rolled, w, "10b: Advance(105) after the jump to 104 == Rolled");
            check(l.LastMonthKnown && l.LastMonthSinceLoad, w, "10b: LastMonthKnown and LastMonthSinceLoad (partial, not full)");
            check(!l.ThisMonthSinceLoad, w, "10b: !ThisMonthSinceLoad (the next month is counted whole)");

            // 11. prune
            int revBefore = l.Revision;
            int removed = l.Prune(new HashSet<EntityRef> { a });
            check(removed == 1, w, "11: Prune({A}) returns 1, got " + removed);
            check(l.TryGet(n, out _), w, "11: Not linked kept");
            check(l.TryGet(a, out _), w, "11: A kept");
            check(!l.TryGet(b, out _), w, "11: B removed");
            check(l.Count == 2 && l.Revision > revBefore, w, "11: Count 2, Revision moved");
            int revPrune = l.Revision;
            check(l.Prune(new HashSet<EntityRef> { a }) == 0 && l.Revision == revPrune, w, "11: a second Prune removes nothing, Revision unchanged");
            l.AddArrival(a, true, 2);
            check(l.TryGet(a, out ea) && ea.This.TouristGuests == 2, w, "11: the index still finds A after a prune");

            // 12. reset
            int revReset = l.Revision;
            l.Reset();
            check(l.Count == 0, w, "12: Reset: Count == 0");
            check(l.MonthKey == SkiResortLedger.NoMonth, w, "12: Reset: MonthKey == NoMonth");
            check(!l.LastMonthKnown && !l.LastMonthSinceLoad && !l.ThisMonthSinceLoad, w, "12: Reset: flags false");
            check(l.Revision > revReset, w, "12: Reset: Revision moved");
            check(l.Advance(7) == Advance.Started && l.ThisMonthSinceLoad, w, "12: after Reset the next Advance is Started");
        }

        private static bool IsZero(SkiResortLedger.Month m)
        {
            return m.TouristGuests == 0 && m.TouristParties == 0 && m.LocalGuests == 0 && m.TicketsPaid == 0 &&
                   m.TicketsUnpaid == 0 && m.TicketsFree == 0 && m.TicketIncome == 0 && m.LiftCharged == 0.0;
        }
    }
}
