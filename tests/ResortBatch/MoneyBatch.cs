// MoneyBatch - tests for tests/ResortBatch: money rules and ledger. `public static class MoneyBatch` with `Run(check)` and its own Near;
// Program.Main calls MoneyBatch.Run(Check). Oracles are literal hand values.
// Covers SkiMoneyRules (constants, lift rates and billing incl. per-type rates, accrual and attribution, ticket price and gate,
// affordability, month keys, guest-day rules, partial months via IsShortMonth / SimulatedPct and Ledger.Advance(key, closedMonthShort),
// LiftAreaRunOpen, lodge spend, season passes, lessons, pre-season pass sale) and SkiResortLedger (start / roll / jump, per-entry counters,
// Revision, Prune, Reset, arrival flags). A guest-day scenario stands in the counter's two day sets with a HashSet and a Dictionary.
// Not testable headless (ECS): the scans, the ticket job, the lift snapshot; the logs and field tests cover them.

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
            GuestDayRules(check);
            LedgerGuestDays(check);
            GuestDayScenario(check);
            PartialMonths(check);
            LiftAreaRuns(check);
            TypeRatesAndSpend(check);
            SeasonPasses(check);
            Lessons(check);
            Presale(check);
        }

        // ── T17 Pre-season pass sale: roll, probability, decision, ledger ──
        // Properties, not hash literals (cannot be computed without running), like T15 / T16.
        private static void Presale(Func<bool, string, string, bool> check)
        {
            const string w = "money T17";
            // The uniform: in [0, 1), deterministic, season- and household-sensitive, roughly uniform, unrelated to the first-arrival roll.
            bool inRange = true, same = true, seasonMatters = false, householdMatters = false;
            double sum = 0;
            int agree = 0, n = 5000;
            for (int hh = 0; hh < n; hh++)
            {
                double u = SkiMoneyRules.PresaleUniform(hh, 2030);
                if (!(u >= 0.0 && u < 1.0))
                    inRange = false;
                if (u != SkiMoneyRules.PresaleUniform(hh, 2030))
                    same = false;
                if (u != SkiMoneyRules.PresaleUniform(hh, 2031))
                    seasonMatters = true;
                if (u != SkiMoneyRules.PresaleUniform(hh + 1, 2030))
                    householdMatters = true;
                sum += u;
                if ((u < 0.5) == SkiMoneyRules.PassRoll(hh, 2030, 50))
                    agree++;
            }
            check(inRange, w, "PresaleUniform is in [0, 1)");
            check(same, w, "the same household and season give the same uniform");
            check(seasonMatters && householdMatters, w, "the season and the household each change the uniform");
            check(Math.Abs(sum / n - 0.5) < 0.02, w, $"mean of PresaleUniform over {n} households {sum / n:F3} (0.5 +- 0.02)");
            double agreeRate = agree / (double)n;
            check(agreeRate > 0.4 && agreeRate < 0.6, w,
                  $"the pre-season roll is unrelated to the first-arrival roll: they agree on {agreeRate:P1} of households at share 50 (not ~100 %)");
            // Band: P(u < p) within 1.5 points of p for a spread of p over 20,000 households.
            bool band = true;
            string bandText = "";
            foreach (double p in new[] { 0.02, 0.05, 0.10, 0.25, 0.60 })
            {
                int hits = 0;
                for (int hh = 0; hh < 20000; hh++)
                    if (SkiMoneyRules.PresaleUniform(hh * 3 + 1, 2031) < p)
                        hits++;
                double rate = hits / 20000.0;
                if (Math.Abs(rate - p) > 0.015)
                {
                    band = false;
                    bandText += $" p {p}: {rate:F4};";
                }
            }
            check(band, w, "P(roll < p) within 1.5 points of p over 20,000 households:" + bandText);

            // Probability = share / 100 x chance, clamped to [0, 1].
            check(Math.Abs(SkiMoneyRules.PresaleProbability(50, 0.1) - 0.05) < 1e-12, w, "PresaleProbability(50, 0.1) = 0.05");
            check(SkiMoneyRules.PresaleProbability(100, 0.6) == 0.6 && SkiMoneyRules.PresaleProbability(100, 1.0) == 1.0, w, "share 100 = the chance itself");
            check(SkiMoneyRules.PresaleProbability(0, 0.5) == 0.0 && SkiMoneyRules.PresaleProbability(-20, 0.5) == 0.0 &&
                  SkiMoneyRules.PresaleProbability(50, 0.0) == 0.0 && SkiMoneyRules.PresaleProbability(50, -0.2) == 0.0 &&
                  SkiMoneyRules.PresaleProbability(50, double.NaN) == 0.0, w, "no share, a debt-like negative or a NaN chance = p 0");
            check(SkiMoneyRules.PresaleProbability(250, 0.5) == 0.5 && SkiMoneyRules.PresaleProbability(100, 7.0) == 1.0, w,
                  "the share is clamped to 100 and p to 1");

            check(SkiMoneyRules.PresaleDecision(5, 2030, 1.0, 0, 9999) == SkiMoneyRules.PassSaleResult.NotEligible, w, "price 0 is NotEligible");
            check(SkiMoneyRules.PresaleDecision(5, 2030, 0.0, 240, 9999) == SkiMoneyRules.PassSaleResult.Declined, w, "p 0 is Declined even when rich");
            check(SkiMoneyRules.PresaleDecision(5, 2030, 1.0, 240, 240) == SkiMoneyRules.PassSaleResult.Bought, w, "p 1 with money == price buys");
            check(SkiMoneyRules.PresaleDecision(5, 2030, 1.0, 240, 239) == SkiMoneyRules.PassSaleResult.CannotPay, w, "p 1 with money == price - 1 cannot pay");
            check(SkiMoneyRules.PresaleDecision(5, 2030, 1.0, 240, 0) == SkiMoneyRules.PassSaleResult.CannotPay &&
                  SkiMoneyRules.PresaleDecision(5, 2030, 1.0, 240, -80) == SkiMoneyRules.PassSaleResult.CannotPay, w, "no money or a debt cannot pay");
            // Expected buyers over 20,000 rich households at p = 0.1: 2,000 +- 150; and a household that rolls out is Declined whatever it holds.
            int buyers = 0, outs = 0;
            bool richOnlyDeclinedOrBought = true;
            for (int hh = 0; hh < 20000; hh++)
            {
                SkiMoneyRules.PassSaleResult res = SkiMoneyRules.PresaleDecision(hh, 2032, 0.1, 240, 1000000);
                if (res == SkiMoneyRules.PassSaleResult.Bought) buyers++;
                else if (res == SkiMoneyRules.PassSaleResult.Declined) outs++;
                else richOnlyDeclinedOrBought = false;
            }
            check(richOnlyDeclinedOrBought && buyers + outs == 20000, w, "a rich household is only ever Bought or Declined");
            check(Math.Abs(buyers - 2000) < 150, w, $"{buyers} buyers of 20,000 rich households at p 0.1 (2,000 +- 150)");
            // Monotone in p: a household that buys at p buys at every higher p.
            bool monotone = true;
            for (int hh = 0; hh < 3000; hh++)
                if (SkiMoneyRules.PresaleDecision(hh, 2033, 0.2, 240, 9999) == SkiMoneyRules.PassSaleResult.Bought &&
                    SkiMoneyRules.PresaleDecision(hh, 2033, 0.4, 240, 9999) != SkiMoneyRules.PassSaleResult.Bought)
                    monotone = false;
            check(monotone, w, "buying at p 0.2 implies buying at p 0.4");

            // Ledger: AddPasses(preSeason) counts the pre-season passes inside the totals (a subset), per month and per season.
            var a = new EntityRef(612, 1);
            var l = new SkiResortLedger();
            l.Advance(800);
            l.AddPasses(a, 4, 960, 0, 2030, true);    // the sale
            l.AddPasses(a, 1, 240, 3, 2030);          // a first-arrival pass + holder guests
            check(l.TryGet(a, out SkiResortLedger.Entry ea) && ea.This.PassesSold == 5 && ea.This.PassIncome == 1200 && ea.This.PassesPre == 4 &&
                  ea.This.PassIncomePre == 960 && ea.This.PassHolderGuests == 3, w,
                  $"month: sold {ea.This.PassesSold} (5), income {ea.This.PassIncome} (1200), pre {ea.This.PassesPre} (4) +{ea.This.PassIncomePre} (960), holder guests {ea.This.PassHolderGuests} (3)");
            check(SkiResortLedger.PassSeasonSoldOf(ea, 2030) == 5 && SkiResortLedger.PassSeasonIncomeOf(ea, 2030) == 1200 &&
                  SkiResortLedger.PassSeasonPreOf(ea, 2030) == 4 && SkiResortLedger.PassSeasonPreIncomeOf(ea, 2030) == 960, w,
                  "season 2030: sold 5 +1200 of which pre 4 +960");
            check(SkiResortLedger.PassSeasonPreOf(ea, 2031) == 0 && SkiResortLedger.PassSeasonPreIncomeOf(ea, 2031) == 0, w, "another season reads 0");
            l.AddPasses(a, 2, 480, 0, 2031, true);    // the next season starts over
            l.TryGet(a, out ea);
            check(SkiResortLedger.PassSeasonSoldOf(ea, 2031) == 2 && SkiResortLedger.PassSeasonPreOf(ea, 2031) == 2 &&
                  SkiResortLedger.PassSeasonPreOf(ea, 2030) == 0, w, "a new season resets the season totals including the pre-season part");
            SkiResortLedger.Month tot = l.Total(false);
            check(tot.PassesSold == 7 && tot.PassesPre == 6 && tot.PassIncomePre == 1440, w,
                  $"Total(this): sold {tot.PassesSold} (7), pre {tot.PassesPre} (6) +{tot.PassIncomePre} (1440)");
            check(l.Advance(801) == Advance.Rolled && l.Total(true).PassesPre == 6 && l.Total(false).PassesPre == 0, w,
                  "a roll moves the pre-season fields to Last and zeroes This");
            // The default call (no flag) leaves the pre-season part alone: v1 callers are unchanged.
            var l2 = new SkiResortLedger();
            l2.Advance(800);
            l2.AddPasses(a, 3, 720, 1, 2030);
            l2.TryGet(a, out SkiResortLedger.Entry e2);
            check(e2.This.PassesPre == 0 && e2.This.PassIncomePre == 0 && SkiResortLedger.PassSeasonPreOf(e2, 2030) == 0 &&
                  e2.This.PassesSold == 3, w, "AddPasses without the flag does not touch the pre-season part");
        }

        // ── T16 Ski-school lessons: constants, price, roll, taken, paid, ledger ──
        // No literal hash oracles (the values cannot be computed without running): the roll is held by properties, like T15.
        private static void Lessons(Func<bool, string, string, bool> check)
        {
            const string w = "money T16";
            check(SkiMoneyRules.LessonShareMax == 50 && SkiMoneyRules.LessonShareStep == 5 && SkiMoneyRules.LessonShareDefault == 30, w,
                  "LessonShare Max / Step / Default = 50 / 5 / 30 (T137 I2, was 20)");
            check(SkiMoneyRules.LessonMultiplierMin == 1.0f && SkiMoneyRules.LessonMultiplierMax == 3.0f &&
                  SkiMoneyRules.LessonMultiplierStep == 0.5f && SkiMoneyRules.LessonMultiplierDefault == 1.5f, w,
                  "LessonMultiplier Min / Max / Step / Default = 1.0 / 3.0 / 0.5 / 1.5");
            check(SkiMoneyRules.ClampLessonShare(-5) == 0 && SkiMoneyRules.ClampLessonShare(25) == 25 && SkiMoneyRules.ClampLessonShare(99) == 50, w,
                  "ClampLessonShare(-5, 25, 99) = 0, 25, 50");
            check(SkiMoneyRules.ClampLessonMultiplier(0.5f) == 1.0f && SkiMoneyRules.ClampLessonMultiplier(2.5f) == 2.5f &&
                  SkiMoneyRules.ClampLessonMultiplier(4f) == 3.0f, w, "ClampLessonMultiplier(0.5, 2.5, 4) = 1.0, 2.5, 3.0");
            check(SkiMoneyRules.ClampLessonMultiplier(float.NaN) == 1.5f && SkiMoneyRules.ClampLessonMultiplier(float.PositiveInfinity) == 1.5f, w,
                  "a non-finite multiplier reads the default 1.5");

            // Price = floor(multiplier x ticket + 0.5), multiplier clamped, 0 without a ticket price.
            check(SkiMoneyRules.LessonPrice(1.5f, 60) == 90, w, "LessonPrice(1.5, 60) " + SkiMoneyRules.LessonPrice(1.5f, 60) + " (90, the new defaults)");
            check(SkiMoneyRules.LessonPrice(1.0f, 60) == 60 && SkiMoneyRules.LessonPrice(3.0f, 60) == 180 && SkiMoneyRules.LessonPrice(2.0f, 45) == 90, w,
                  "LessonPrice(1.0, 60) = 60, (3.0, 60) = 180, (2.0, 45) = 90");
            check(SkiMoneyRules.LessonPrice(1.5f, 5) == 8 && SkiMoneyRules.LessonPrice(1.5f, 40) == 60, w,
                  "half up: LessonPrice(1.5, 5) " + SkiMoneyRules.LessonPrice(1.5f, 5) + " (8), (1.5, 40) " + SkiMoneyRules.LessonPrice(1.5f, 40) + " (60)");
            check(SkiMoneyRules.LessonPrice(1.5f, 0) == 0 && SkiMoneyRules.LessonPrice(1.5f, -5) == 0, w, "no ticket price = no lesson price");
            check(SkiMoneyRules.LessonPrice(0.2f, 60) == 60 && SkiMoneyRules.LessonPrice(9f, 60) == 180, w,
                  "the multiplier is clamped: (0.2, 60) = 60, (9, 60) = 180");
            // Never cheaper than the ticket it follows, for every slider position.
            bool atLeastTicket = true;
            for (int p = 0; p <= SkiMoneyRules.TicketPriceMax; p += SkiMoneyRules.TicketPriceStep)
                for (float m = SkiMoneyRules.LessonMultiplierMin; m <= SkiMoneyRules.LessonMultiplierMax; m += SkiMoneyRules.LessonMultiplierStep)
                    if (SkiMoneyRules.LessonPrice(m, p) < p)
                        atLeastTicket = false;
            check(atLeastTicket, w, "LessonPrice is never below the ticket price (multiplier >= 1) at any slider position");

            // The roll: share 0 never, >= 100 always, deterministic, monotone in the share, sensitive to day and person.
            bool never = true, always = true, same = true, monotone = true, dayMatters = false, personMatters = false;
            for (int hh = 1; hh <= 400; hh++)
            {
                if (SkiMoneyRules.LessonRoll(hh, 5000, 0, 0) || SkiMoneyRules.LessonRoll(hh, 5000, 0, -10))
                    never = false;
                if (!SkiMoneyRules.LessonRoll(hh, 5000, 1, 100) || !SkiMoneyRules.LessonRoll(hh, 5000, 1, 250))
                    always = false;
                if (SkiMoneyRules.LessonRoll(hh, 5000, 2, 20) != SkiMoneyRules.LessonRoll(hh, 5000, 2, 20))
                    same = false;
                for (int s = 5; s < 100; s += 5)
                    if (SkiMoneyRules.LessonRoll(hh, 5000, 0, s) && !SkiMoneyRules.LessonRoll(hh, 5000, 0, s + 5))
                        monotone = false;
                if (SkiMoneyRules.LessonRoll(hh, 5000, 0, 50) != SkiMoneyRules.LessonRoll(hh, 5001, 0, 50))
                    dayMatters = true;
                if (SkiMoneyRules.LessonRoll(hh, 5000, 0, 50) != SkiMoneyRules.LessonRoll(hh, 5000, 1, 50))
                    personMatters = true;
            }
            check(never, w, "share <= 0 never rolls true");
            check(always, w, "share >= 100 always rolls true");
            check(same, w, "the same inputs give the same roll");
            check(monotone, w, "a roll true at a share is true at every higher share");
            check(dayMatters, w, "the day key changes some rolls (a household is not locked in or out for ever)");
            check(personMatters, w, "the person index changes some rolls (a party's members roll apart)");
            // Rate: at shares 5..50 over 20,000 households the share of true rolls sits within 3 points of the setting.
            bool band = true;
            string bandText = "";
            for (int s = 5; s <= 50; s += 5)
            {
                int hits = 0;
                for (int hh = 0; hh < 20000; hh++)
                    if (SkiMoneyRules.LessonRoll(hh * 7 + 3, 6100, hh % 4, s))
                        hits++;
                double rate = hits / 20000.0 * 100.0;
                if (Math.Abs(rate - s) > 3.0)
                {
                    band = false;
                    bandText += $" share {s}: {rate:F1} %;";
                }
            }
            check(band, w, "roll rate within 3 points of the share at 5..50 over 20,000 households:" + bandText);

            // LessonsTaken: people x roll, one lesson per person at most.
            check(SkiMoneyRules.LessonsTaken(0, 1, 5000, 100) == 0 && SkiMoneyRules.LessonsTaken(-3, 1, 5000, 100) == 0, w, "no people, no lessons");
            check(SkiMoneyRules.LessonsTaken(4, 9, 5000, 100) == 4 && SkiMoneyRules.LessonsTaken(4, 9, 5000, 0) == 0, w,
                  "share 100 = every person, share 0 = nobody");
            check(SkiMoneyRules.LessonsTaken(500, 9, 5000, 100) == SkiMoneyRules.LessonPeopleGuard, w,
                  "an absurd party is capped at LessonPeopleGuard " + SkiMoneyRules.LessonPeopleGuard);
            bool sumOk = true, boundOk = true;
            double meanSum = 0;
            for (int hh = 0; hh < 5000; hh++)
            {
                int expect = 0;
                for (int p = 0; p < 3; p++)
                    if (SkiMoneyRules.LessonRoll(hh, 7200, p, 20))
                        expect++;
                int got = SkiMoneyRules.LessonsTaken(3, hh, 7200, 20);
                if (got != expect)
                    sumOk = false;
                if (got < 0 || got > 3)
                    boundOk = false;
                meanSum += got;
            }
            check(sumOk, w, "LessonsTaken(n) = the count of the n person rolls");
            check(boundOk, w, "LessonsTaken is within 0..people");
            double perPerson = meanSum / (5000.0 * 3.0);
            check(Math.Abs(perPerson - 0.20) < 0.02, w, $"parties of 3 at share 20: {perPerson:F3} lessons per person (0.20 +- 0.02)");

            // LessonsPaid: min(taken, money / price), never into debt.
            check(SkiMoneyRules.LessonsPaid(3, 90, 270) == 3 && SkiMoneyRules.LessonsPaid(3, 90, 269) == 2 && SkiMoneyRules.LessonsPaid(3, 90, 89) == 0, w,
                  "LessonsPaid(3, 90, 270 / 269 / 89) = 3 / 2 / 0");
            check(SkiMoneyRules.LessonsPaid(3, 90, 0) == 0 && SkiMoneyRules.LessonsPaid(3, 90, -5) == 0 && SkiMoneyRules.LessonsPaid(0, 90, 999) == 0 &&
                  SkiMoneyRules.LessonsPaid(3, 0, 999) == 0, w, "no money, a debt, nobody or no price = no lessons paid");
            bool coveredOk = true;
            for (int taken = 0; taken <= 6; taken++)
                for (int money = -10; money <= 600; money += 7)
                {
                    int paidN = SkiMoneyRules.LessonsPaid(taken, 90, money);
                    if (paidN < 0 || paidN > taken || (paidN > 0 && (long)paidN * 90 > money))
                        coveredOk = false;
                }
            check(coveredOk, w, "LessonsPaid never charges more than the household holds (taken 0-6, money -10..600, price 90)");

            // Ledger: AddLessons books under the AddTickets rules, carries into Total, moves to Last with the month.
            var a = new EntityRef(512, 1);
            var b = new EntityRef(513, 1);
            var l = new SkiResortLedger();
            l.Advance(700);
            int rev0 = l.Revision;
            l.AddLessons(a, 0, 0);
            l.AddLessons(a, -2, -90);
            check(l.Revision == rev0 && l.Count == 0, w, "an all-zero (or negative) AddLessons changes nothing and creates no entry");
            l.AddLessons(a, 2, 180);
            l.AddLessons(a, 1, 90);
            l.AddLessons(b, 3, 270);
            check(l.TryGet(a, out SkiResortLedger.Entry ea) && ea.This.Lessons == 3 && ea.This.LessonIncome == 270, w,
                  $"ski area a: lessons {ea.This.Lessons} (3), income {ea.This.LessonIncome} (270)");
            check(l.Revision > rev0, w, "AddLessons with a number moves Revision");
            SkiResortLedger.Month tot = l.Total(false);
            check(tot.Lessons == 6 && tot.LessonIncome == 540, w, $"Total(this): lessons {tot.Lessons} (6), income {tot.LessonIncome} (540)");
            check(l.Advance(701) == Advance.Rolled && l.Total(true).Lessons == 6 && l.Total(true).LessonIncome == 540 && l.Total(false).Lessons == 0, w,
                  "a roll moves the lesson fields to Last and zeroes This");
            // A lesson does not touch the ticket or pass fields.
            check(tot.TicketIncome == 0 && tot.PassIncome == 0 && tot.LodgeSpendIncome == 0, w, "lessons leave tickets, passes and lodge spend alone");
        }

        // ── T15 Season passes: season key, price, roll, standing, sale ──
        private static void SeasonPasses(Func<bool, string, string, bool> check)
        {
            const string w = "money T15";
            // Season key: July (7) and later = the year; January..June = the year before; December and the next March share it.
            check(SkiMoneyRules.SeasonKeyOf(2030, 7) == 2030, w, "SeasonKeyOf(2030, 7) " + SkiMoneyRules.SeasonKeyOf(2030, 7));
            check(SkiMoneyRules.SeasonKeyOf(2030, 6) == 2029, w, "SeasonKeyOf(2030, 6) " + SkiMoneyRules.SeasonKeyOf(2030, 6));
            check(SkiMoneyRules.SeasonKeyOf(2030, 12) == 2030, w, "SeasonKeyOf(2030, 12) " + SkiMoneyRules.SeasonKeyOf(2030, 12));
            check(SkiMoneyRules.SeasonKeyOf(2031, 3) == 2030, w, "SeasonKeyOf(2031, 3) " + SkiMoneyRules.SeasonKeyOf(2031, 3));
            check(SkiMoneyRules.SeasonKeyOf(2031, 1) == 2030, w, "SeasonKeyOf(2031, 1) " + SkiMoneyRules.SeasonKeyOf(2031, 1));
            check(SkiMoneyRules.SeasonKeyOf(2031, 0) == 2030 && SkiMoneyRules.SeasonKeyOf(2031, 13) == 2031, w,
                  "a month outside 1..12 is clamped: (2031, 0) " + SkiMoneyRules.SeasonKeyOf(2031, 0) + ", (2031, 13) " + SkiMoneyRules.SeasonKeyOf(2031, 13));
            // From the clock: normalizedDate 0.5 = month index 6 = July (calendar month 7); 0.49 = June; 0.99 = December.
            check(SkiMoneyRules.SeasonKeyOfDate(2030, 0.5f) == 2030, w, "SeasonKeyOfDate(2030, 0.5) " + SkiMoneyRules.SeasonKeyOfDate(2030, 0.5f));
            check(SkiMoneyRules.SeasonKeyOfDate(2030, 0.49f) == 2029, w, "SeasonKeyOfDate(2030, 0.49) " + SkiMoneyRules.SeasonKeyOfDate(2030, 0.49f));
            check(SkiMoneyRules.SeasonKeyOfDate(2030, 0.99f) == 2030, w, "SeasonKeyOfDate(2030, 0.99) " + SkiMoneyRules.SeasonKeyOfDate(2030, 0.99f));
            check(SkiMoneyRules.SeasonKeyOfDate(2031, 0.2f) == 2030, w, "SeasonKeyOfDate(2031, 0.2) " + SkiMoneyRules.SeasonKeyOfDate(2031, 0.2f));
            check(SkiMoneyRules.SeasonKeyOfDate(2031, float.NaN) == 2030, w, "SeasonKeyOfDate(2031, NaN) reads month 1: " + SkiMoneyRules.SeasonKeyOfDate(2031, float.NaN));

            // Price: multiplier × ticket, multiplier clamped 2..10, nothing without a ticket price.
            check(SkiMoneyRules.PassPrice(4, 40) == 160, w, "PassPrice(4, 40) " + SkiMoneyRules.PassPrice(4, 40));
            check(SkiMoneyRules.PassPrice(10, 200) == 2000, w, "PassPrice(10, 200) " + SkiMoneyRules.PassPrice(10, 200));
            check(SkiMoneyRules.PassPrice(2, 5) == 10, w, "PassPrice(2, 5) " + SkiMoneyRules.PassPrice(2, 5));
            check(SkiMoneyRules.PassPrice(1, 40) == 80 && SkiMoneyRules.PassPrice(99, 40) == 400, w,
                  "multiplier clamped: (1, 40) " + SkiMoneyRules.PassPrice(1, 40) + ", (99, 40) " + SkiMoneyRules.PassPrice(99, 40));
            check(SkiMoneyRules.PassPrice(4, 0) == 0 && SkiMoneyRules.PassPrice(4, -5) == 0, w, "PassPrice with no ticket price should be 0");

            // Roll: 0 never, 100 always, deterministic, monotone in the share, and about the share over many households.
            check(!SkiMoneyRules.PassRoll(77, 2030, 0) && !SkiMoneyRules.PassRoll(77, 2030, -10), w, "share <= 0 should never roll");
            check(SkiMoneyRules.PassRoll(77, 2030, 100) && SkiMoneyRules.PassRoll(77, 2030, 250), w, "share >= 100 should always roll");
            bool deterministic = true, monotone = true, differsBySeason = false, differsByHousehold = false;
            int[] shares = { 5, 25, 50, 75, 95 };
            int[] hits = new int[shares.Length];
            const int n = 20000;
            for (int i = 0; i < n; i++)
            {
                bool prev = false;
                for (int s = 0; s < shares.Length; s++)
                {
                    bool r = SkiMoneyRules.PassRoll(i, 2030, shares[s]);
                    deterministic &= r == SkiMoneyRules.PassRoll(i, 2030, shares[s]);
                    monotone &= !prev || r;   // true at a lower share implies true at every higher one
                    prev = r;
                    if (r) hits[s]++;
                }
                differsBySeason |= SkiMoneyRules.PassRoll(i, 2030, 50) != SkiMoneyRules.PassRoll(i, 2031, 50);
                differsByHousehold |= SkiMoneyRules.PassRoll(i, 2030, 50) != SkiMoneyRules.PassRoll(i + 1, 2030, 50);
            }
            check(deterministic, w, "PassRoll gave two answers for the same inputs");
            check(monotone, w, "PassRoll is not monotone in the share");
            check(differsBySeason, w, "PassRoll never changes with the season key (a household would be stuck with one answer)");
            check(differsByHousehold, w, "PassRoll never changes with the household index");
            for (int s = 0; s < shares.Length; s++)
            {
                // Expected n × share / 100 with a ±3 % of n band (n = 20,000: ±600, over 6 sigma at share 50).
                int expected = n * shares[s] / 100;
                check(Math.Abs(hits[s] - expected) <= n * 3 / 100, w,
                      "PassRoll at share " + shares[s] + " hit " + hits[s] + " of " + n + ", expected about " + expected);
            }

            // Standing: none without a component or with another season's, Declined by the flag, else Holder.
            check(SkiMoneyRules.PassStandingOf(false, 0, 0, 2030) == SkiMoneyRules.PassStanding.None, w, "no component should be None");
            check(SkiMoneyRules.PassStandingOf(true, 2029, 0, 2030) == SkiMoneyRules.PassStanding.None, w, "last season's pass should be None");
            check(SkiMoneyRules.PassStandingOf(true, 2029, SkiMoneyRules.PassFlagDeclined, 2030) == SkiMoneyRules.PassStanding.None, w,
                  "last season's Declined should be None");
            check(SkiMoneyRules.PassStandingOf(true, 2030, 0, 2030) == SkiMoneyRules.PassStanding.Holder, w, "this season's pass should be Holder");
            check(SkiMoneyRules.PassStandingOf(true, 2030, SkiMoneyRules.PassFlagDeclined, 2030) == SkiMoneyRules.PassStanding.Declined, w,
                  "this season's Declined flag should be Declined");
            check(SkiMoneyRules.PassFlagDeclined == 1, w, "PassFlagDeclined is the serialized bit 1 (save-format contract)");

            // Sale: eligibility first, then the roll, then the money (never into debt).
            check(SkiMoneyRules.PassSale(false, 100, 5, 2030, 160, 9999) == SkiMoneyRules.PassSaleResult.NotEligible, w, "a tourist is NotEligible");
            check(SkiMoneyRules.PassSale(true, 0, 5, 2030, 160, 9999) == SkiMoneyRules.PassSaleResult.NotEligible, w, "share 0 is NotEligible");
            check(SkiMoneyRules.PassSale(true, 100, 5, 2030, 0, 9999) == SkiMoneyRules.PassSaleResult.NotEligible, w, "price 0 is NotEligible");
            check(SkiMoneyRules.PassSale(true, 100, 5, 2030, 160, 160) == SkiMoneyRules.PassSaleResult.Bought, w, "money == price should buy");
            check(SkiMoneyRules.PassSale(true, 100, 5, 2030, 160, 159) == SkiMoneyRules.PassSaleResult.CannotPay, w, "money == price - 1 cannot pay");
            check(SkiMoneyRules.PassSale(true, 100, 5, 2030, 160, 0) == SkiMoneyRules.PassSaleResult.CannotPay, w, "no money cannot pay");
            check(SkiMoneyRules.PassSale(true, 100, 5, 2030, 160, -50) == SkiMoneyRules.PassSaleResult.CannotPay, w, "a household in debt cannot pay");
            // A household that rolls out is Declined whatever it holds; one that rolls in with money buys (found by scanning).
            int declinedIdx = -1, boughtIdx = -1;
            for (int i = 0; i < 1000 && (declinedIdx < 0 || boughtIdx < 0); i++)
            {
                if (SkiMoneyRules.PassRoll(i, 2030, 50)) { if (boughtIdx < 0) boughtIdx = i; }
                else if (declinedIdx < 0) declinedIdx = i;
            }
            check(declinedIdx >= 0 && boughtIdx >= 0, w, "no household in 0..999 rolled in and out at share 50");
            check(SkiMoneyRules.PassSale(true, 50, declinedIdx, 2030, 160, 999999) == SkiMoneyRules.PassSaleResult.Declined, w,
                  "a household that rolls out is Declined even when rich");
            check(SkiMoneyRules.PassSale(true, 50, boughtIdx, 2030, 160, 999999) == SkiMoneyRules.PassSaleResult.Bought, w,
                  "a household that rolls in with money buys");
            check(SkiMoneyRules.PassSale(true, 50, boughtIdx, 2030, 160, 10) == SkiMoneyRules.PassSaleResult.CannotPay, w,
                  "a household that rolls in without the money cannot pay");

            // Ledger: AddPasses books under the AddTickets rules (inputs below 0 read 0, all-zero changes nothing), carries into Total
            // and moves to Last with the month.
            var a = new EntityRef(412, 1);
            var b = new EntityRef(413, 1);
            var l = new SkiResortLedger();
            l.Advance(500);
            int rev0 = l.Revision;
            l.AddPasses(a, 0, 0, 0);
            l.AddPasses(a, -3, -100, -2);
            check(l.Revision == rev0 && l.Count == 0, w, "an all-zero (or negative) AddPasses should change nothing and create no entry");
            l.AddPasses(a, 2, 320, 5);
            l.AddPasses(a, 1, 160);
            l.AddPasses(b, 0, 0, 4);
            check(l.TryGet(a, out SkiResortLedger.Entry ea) && ea.This.PassesSold == 3 && ea.This.PassIncome == 480 && ea.This.PassHolderGuests == 5, w,
                  $"ski area a: sold {ea.This.PassesSold} (3), income {ea.This.PassIncome} (480), holder guests {ea.This.PassHolderGuests} (5)");
            check(l.TryGet(b, out SkiResortLedger.Entry eb) && eb.This.PassesSold == 0 && eb.This.PassIncome == 0 && eb.This.PassHolderGuests == 4, w,
                  $"ski area b: holder guests only: {eb.This.PassHolderGuests} (4)");
            check(l.Revision > rev0, w, "AddPasses with a number should move Revision");
            SkiResortLedger.Month tot = l.Total(false);
            check(tot.PassesSold == 3 && tot.PassIncome == 480 && tot.PassHolderGuests == 9, w,
                  $"Total(this): sold {tot.PassesSold} (3), income {tot.PassIncome} (480), holder guests {tot.PassHolderGuests} (9)");
            check(l.Advance(501) == Advance.Rolled && l.Total(true).PassesSold == 3 && l.Total(false).PassesSold == 0 && l.Total(true).PassHolderGuests == 9, w,
                  "a roll should move the pass fields to Last and zero This");

            // Ski days (P3): a pass holder gets P = 1 in place of PriceFactor; the existing 10-argument Chance is the holder-false
            // 11-argument one bit for bit (PriceFactor(40) is exactly 1, PriceFactor(200) is 0.2, SkiDayTests G / Y).
            SkiDayRules.Knobs def = SkiDayRules.DefaultKnobs();
            const SkiDayRules.GuestKind res = SkiDayRules.GuestKind.Resident;
            double holder200 = SkiDayRules.Chance(res, 75, 3.36, 1.0, 0.0, 200, 0, 1.0, 1.0, def, true);
            double plain40 = SkiDayRules.Chance(res, 75, 3.36, 1.0, 0.0, 40, 0, 1.0, 1.0, def, false);
            double plain200 = SkiDayRules.Chance(res, 75, 3.36, 1.0, 0.0, 200, 0, 1.0, 1.0, def, false);
            check(holder200 > 0.0 && BitConverter.DoubleToInt64Bits(holder200) == BitConverter.DoubleToInt64Bits(plain40), w,
                  $"a holder at price 200 should chance exactly what a non-holder does at price 40 (P = 1): {holder200} vs {plain40}");
            check(Math.Abs(plain200 - holder200 * 0.2) < 1e-12, w, $"a non-holder at price 200 is 0.2 of the holder: {plain200} vs {holder200 * 0.2}");
            bool sameBits = true;
            int[] prices = { 0, 5, 40, 41, 80, 100, 200 };
            for (int i = 0; i < prices.Length; i++)
            {
                double ten = SkiDayRules.Chance(res, 75, 3.36, 1.0, 0.3, prices[i], 20, 1.2, 0.7, def);
                double eleven = SkiDayRules.Chance(res, 75, 3.36, 1.0, 0.3, prices[i], 20, 1.2, 0.7, def, false);
                sameBits &= BitConverter.DoubleToInt64Bits(ten) == BitConverter.DoubleToInt64Bits(eleven);
                // at or below the full-price threshold a holder gains nothing
                double holder = SkiDayRules.Chance(res, 75, 3.36, 1.0, 0.3, prices[i], 20, 1.2, 0.7, def, true);
                check(holder >= eleven, w, $"a holder should never chance less than a non-holder (price {prices[i]}): {holder} vs {eleven}");
            }
            check(sameBits, w, "the 10-argument Chance should equal the 11-argument one with passHolder false, bit for bit");

            // Constants (the Options sliders and the defaults).
            check(SkiMoneyRules.PassMultiplierMin == 2 && SkiMoneyRules.PassMultiplierMax == 10 && SkiMoneyRules.PassMultiplierDefault == 2, w,
                  "multiplier constants 2..10, default 2 (T137 I3, was 4)");
            check(SkiMoneyRules.PassShareMax == 100 && SkiMoneyRules.PassShareStep == 5 && SkiMoneyRules.PassShareDefault == 50, w,
                  "share constants 0..100 step 5, default 50");
            check(SkiMoneyRules.ClampPassShare(-5) == 0 && SkiMoneyRules.ClampPassShare(130) == 100 && SkiMoneyRules.ClampPassShare(55) == 55, w,
                  "ClampPassShare");
            check(SkiMoneyRules.ClampPassMultiplier(0) == 2 && SkiMoneyRules.ClampPassMultiplier(11) == 10 && SkiMoneyRules.ClampPassMultiplier(6) == 6, w,
                  "ClampPassMultiplier");
        }

        // ── T14 per-type lift rates, lodge spend ──
        private static void TypeRatesAndSpend(Func<bool, string, string, bool> check)
        {
            const string w = "money T14";
            // The type's rate: fixed + perKm × km (literal oracles).
            check(SkiMoneyRules.LiftFullRate(1500f, 5000f, 2000f) == 8000f, w,
                  "LiftFullRate(1500, 5000, 2000) " + SkiMoneyRules.LiftFullRate(1500f, 5000f, 2000f));
            check(SkiMoneyRules.LiftRate(Billing.Unfinished, 1500f, 5000f, 2000f) == 2000f, w,
                  "LiftRate(Unfinished, 1500, 5000, 2000) " + SkiMoneyRules.LiftRate(Billing.Unfinished, 1500f, 5000f, 2000f));
            check(SkiMoneyRules.LiftFullRate(2000f, 8000f, 3000f) == 14000f, w,
                  "LiftFullRate(2000, 8000, 3000) " + SkiMoneyRules.LiftFullRate(2000f, 8000f, 3000f));
            check(SkiMoneyRules.LiftFullRate(3000f, 11000f, 4500f) == 24500f, w,
                  "LiftFullRate(3000, 11000, 4500) " + SkiMoneyRules.LiftFullRate(3000f, 11000f, 4500f));
            check(SkiMoneyRules.LiftRate(Billing.Full, 3000f, 11000f, 4500f) == 24500f, w, "LiftRate(Full, 3000, 11000, 4500)");
            check(SkiMoneyRules.LiftRate(Billing.NoOpenRun, 2000f, 8000f, 3000f) == 3500f, w, "LiftRate(NoOpenRun, 2000, 8000, 3000)");
            // The per-type no-open-run share (the tram 50 %); Unfinished keeps 25 %; a bad share reads 25 %.
            check(SkiMoneyRules.ShuttleNoOpenRunShare == 0.5f, w, "ShuttleNoOpenRunShare 0.5 (T137 S1)");
            check(SkiMoneyRules.LiftRate(Billing.NoOpenRun, 2233f, 11000f, 4500f, 0.5f) == 10524.25f, w,
                  "the playthrough tram 2,233 m off-season: 10,524.25 (half of 21,048.5; was 21,048.5 full) " +
                  SkiMoneyRules.LiftRate(Billing.NoOpenRun, 2233f, 11000f, 4500f, 0.5f));
            check(SkiMoneyRules.LiftRate(Billing.Unfinished, 2233f, 11000f, 4500f, 0.5f) == 5262.125f, w,
                  "Unfinished keeps the 25 % share whatever the type's no-open-run share");
            check(SkiMoneyRules.LiftRate(Billing.Full, 2233f, 11000f, 4500f, 0.5f) == 21048.5f, w, "Full ignores the share");
            check(SkiMoneyRules.LiftRate(Billing.NoOpenRun, 2000f, 8000f, 3000f, float.NaN) == 3500f &&
                  SkiMoneyRules.LiftRate(Billing.NoOpenRun, 2000f, 8000f, 3000f, 1.5f) == 3500f &&
                  SkiMoneyRules.LiftRate(Billing.NoOpenRun, 2000f, 8000f, 3000f, 0.25f) == SkiMoneyRules.LiftRate(Billing.NoOpenRun, 2000f, 8000f, 3000f),
                  w, "a share that is NaN or outside 0..1 reads 25 %; 0.25 = the four-argument overload");
            check(SkiMoneyRules.LiftFullRate(float.NaN, 5000f, 2000f) == 5000f && SkiMoneyRules.LiftFullRate(-5f, 5000f, 2000f) == 5000f,
                  w, "a non-finite or negative length counts as 0 m");
            check(SkiMoneyRules.LiftFullRate(1000f, float.NaN, -2000f) == 0f, w, "non-finite / negative rate parts read 0");

            // The one-length overloads are the fixed-grip row's (T1-T7 unchanged): ≡ the 3-argument form with 2500 + 1000.
            float[] lengths = { 0f, 991f, 1000f, 2281f, -5f, float.NaN, float.PositiveInfinity };
            Billing[] billings = { Billing.Full, Billing.NoPower, Billing.Unfinished, Billing.NoOpenRun };
            bool same = true;
            string first = "";
            for (int i = 0; i < lengths.Length; i++)
            {
                if (SkiMoneyRules.LiftFullRate(lengths[i]) != SkiMoneyRules.LiftFullRate(lengths[i], 2500f, 1000f) && same)
                {
                    same = false;
                    first = "LiftFullRate(" + lengths[i] + ")";
                }
                for (int b = 0; b < billings.Length; b++)
                {
                    if (SkiMoneyRules.LiftRate(billings[b], lengths[i]) !=
                        SkiMoneyRules.LiftRate(billings[b], lengths[i], 2500f, 1000f) && same)
                    {
                        same = false;
                        first = "LiftRate(" + billings[b] + ", " + lengths[i] + ")";
                    }
                }
            }
            check(same, w, "the fixed-grip overloads ≡ (…, 2500, 1000): " + first);

            // Every catalog row's rate at 1 km: 3,500 / 7,000 / 11,000 / 15,500.
            float[] perKm1 = { 3500f, 7000f, 11000f, 15500f };
            IReadOnlyList<LiftTypeDef> lifts = SkiCatalog.Lifts;
            check(lifts.Count == perKm1.Length, w, "SkiCatalog.Lifts has " + lifts.Count + " rows, the oracle 4");
            for (int t = 0; t < lifts.Count && t < perKm1.Length; t++)
            {
                float got = SkiMoneyRules.LiftFullRate(1000f, lifts[t].FixedPerMonth, lifts[t].PerKmPerMonth);
                check(got == perKm1[t], w, lifts[t].Id + " LiftFullRate(1 km) " + got + ", want " + perKm1[t]);
            }

            check(SkiMoneyRules.LodgeSpendDefault == 15 && SkiMoneyRules.LodgeSpendMax == 50 && SkiMoneyRules.LodgeSpendStep == 5, w,
                  "LodgeSpendDefault / Max / Step = 15 / 50 / 5 (15 since econ-2 round 2; was 0)");
            // The default per tier (x1 / x1.5 / x3, rounded half up) = 15 / 23 / 45 per paying guest.
            check(SkiMoneyRules.LodgeSpendOf(SkiMoneyRules.LodgeSpendDefault, 1.0f) == 15 &&
                  SkiMoneyRules.LodgeSpendOf(SkiMoneyRules.LodgeSpendDefault, 1.5f) == 23 &&
                  SkiMoneyRules.LodgeSpendOf(SkiMoneyRules.LodgeSpendDefault, 3.0f) == 45, w,
                  "LodgeSpendOf(default 15) = 15 / 23 / 45 at x1 / x1.5 / x3");
            // Slider alignment: the default sits on the slider step.
            check(SkiMoneyRules.LodgeSpendDefault % SkiMoneyRules.LodgeSpendStep == 0 &&
                  SkiMoneyRules.TicketPriceDefault % SkiMoneyRules.TicketPriceStep == 0, w,
                  "defaults sit on their slider steps (ticket 60 / 5, lodge spend 15 / 5)");
            check(SkiMoneyRules.ClampLodgeSpend(-5) == 0 && SkiMoneyRules.ClampLodgeSpend(25) == 25 && SkiMoneyRules.ClampLodgeSpend(55) == 50,
                  w, "ClampLodgeSpend(-5, 25, 55) = 0, 25, 50");
            SpendOf(check, w, 10, 1.5f, 15);
            SpendOf(check, w, 5, 1.5f, 8);    // floor(7.5 + 0.5)
            SpendOf(check, w, 0, 3f, 0);
            SpendOf(check, w, 10, 0f, 0);
            SpendOf(check, w, -5, 1f, 0);
            SpendOf(check, w, 10, float.NaN, 0);
            SpendOf(check, w, 50, 3f, 150);
            SpendOf(check, w, 5, 1f, 5);
            Paid(check, w, 4, 30, 100, 3);
            Paid(check, w, 4, 30, 0, 0);
            Paid(check, w, 0, 30, 1000, 0);
            Paid(check, w, 4, 0, 1000, 0);
            Paid(check, w, 4, 30, 1000, 4);
            Paid(check, w, 4, 30, -10, 0);
        }

        private static void SpendOf(Func<bool, string, string, bool> check, string w, int setting, float multiplier, int expected)
        {
            int got = SkiMoneyRules.LodgeSpendOf(setting, multiplier);
            check(got == expected, w, $"LodgeSpendOf({setting}, {multiplier}) {got}, want {expected}");
        }

        private static void Paid(Func<bool, string, string, bool> check, string w, int paid, int spend, int money, int expected)
        {
            int got = SkiMoneyRules.SpendPaid(paid, spend, money);
            check(got == expected, w, $"SpendPaid({paid}, {spend}, {money}) {got}, want {expected}");
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
            check(SkiMoneyRules.TicketPriceDefault == 80, w, "TicketPriceDefault " + SkiMoneyRules.TicketPriceDefault + " (80 since T137 I1; 60 since econ-2 round 2)");
            check(SkiMoneyRules.TicketPriceMax == 200, w, "TicketPriceMax " + SkiMoneyRules.TicketPriceMax);
            check(SkiMoneyRules.TicketPriceStep == 5, w, "TicketPriceStep " + SkiMoneyRules.TicketPriceStep);
            check(SkiMoneyRules.TicksPerDay == 262144, w, "TicksPerDay " + SkiMoneyRules.TicksPerDay);
            check((int)Billing.Full == 0 && (int)Billing.NoPower == 1 && (int)Billing.Unfinished == 2 &&
                  (int)Billing.NoOpenRun == 3, w, "LiftBilling literal values 0-3");
            check(SkiMoneyRules.GuestDayOuting == 1, w, "GuestDayOuting " + SkiMoneyRules.GuestDayOuting);
            check(SkiMoneyRules.GuestDayLeisure == 2, w, "GuestDayLeisure " + SkiMoneyRules.GuestDayLeisure);
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

            // The third argument is LiftAreaRunOpen; precedence unfinished, no power, no counted run.
            check(SkiMoneyRules.LiftBillingOf(true, true, true) == Billing.Full, w, "LiftBillingOf(complete, powered, run open)");
            check(SkiMoneyRules.LiftBillingOf(true, true, false) == Billing.NoOpenRun, w, "LiftBillingOf(complete, powered, no run)");
            check(SkiMoneyRules.LiftBillingOf(true, false, false) == Billing.NoPower, w, "LiftBillingOf(complete, no power, no run)");
            check(SkiMoneyRules.LiftBillingOf(true, false, true) == Billing.NoPower, w, "LiftBillingOf(complete, no power, run open)");
            check(SkiMoneyRules.LiftBillingOf(false, true, false) == Billing.Unfinished, w, "LiftBillingOf(open, powered, no run)");
            check(SkiMoneyRules.LiftBillingOf(false, false, true) == Billing.Unfinished, w, "LiftBillingOf(open, no power, run open)");

            check(SkiMoneyRules.LiftRate(Billing.Full, 1000f) == 3500f, w, "LiftRate(Full, 1000) " + SkiMoneyRules.LiftRate(Billing.Full, 1000f));
            check(SkiMoneyRules.LiftRate(Billing.NoPower, 1000f) == 875f, w, "LiftRate(NoPower, 1000) " + SkiMoneyRules.LiftRate(Billing.NoPower, 1000f));
            check(SkiMoneyRules.LiftRate(Billing.Unfinished, 0f) == 625f, w, "LiftRate(Unfinished, 0) " + SkiMoneyRules.LiftRate(Billing.Unfinished, 0f));
            check(SkiMoneyRules.LiftRate(Billing.Unfinished, 991f) == 872.75f, w,
                  "LiftRate(Unfinished, 991) " + SkiMoneyRules.LiftRate(Billing.Unfinished, 991f));
            check(SkiMoneyRules.LiftRate(Billing.NoPower, 2281f) == 1195.25f, w,
                  "LiftRate(NoPower, 2281) " + SkiMoneyRules.LiftRate(Billing.NoPower, 2281f));
            // NoOpenRun bills the reduced share like the other non-Full billings.
            check(SkiMoneyRules.LiftRate(Billing.NoOpenRun, 1000f) == 875f, w,
                  "LiftRate(NoOpenRun, 1000) " + SkiMoneyRules.LiftRate(Billing.NoOpenRun, 1000f));
            check(SkiMoneyRules.LiftRate(Billing.NoOpenRun, 2281f) == 1195.25f, w,
                  "LiftRate(NoOpenRun, 2281) " + SkiMoneyRules.LiftRate(Billing.NoOpenRun, 2281f));
            check(SkiMoneyRules.LiftRate(Billing.NoOpenRun, 0f) == 625f, w,
                  "LiftRate(NoOpenRun, 0) " + SkiMoneyRules.LiftRate(Billing.NoOpenRun, 0f));

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

            // Four arguments; known = score present and not held (a running lift alone does not open the gate).
            GateCase(check, w, 0, 1f, true, 3, Gate.Off);
            GateCase(check, w, 0, 0f, false, 0, Gate.Off);                  // price first
            GateCase(check, w, 40, 0f, true, 3, Gate.NoEfficiency);
            GateCase(check, w, 40, float.NaN, true, 3, Gate.NoEfficiency);
            GateCase(check, w, 40, 0.01f, true, 0, Gate.NothingOpen);
            GateCase(check, w, 40, 1f, false, 0, Gate.NotKnownYet);
            GateCase(check, w, 40, 1f, false, 3, Gate.NotKnownYet);         // unknown wins over runs
            GateCase(check, w, 40, 0.04f, true, 1, Gate.Open);
            GateCase(check, w, 40, 1.25f, true, 0, Gate.NothingOpen); // no operating-lift branch
            GateCase(check, w, 40, 0.99f, true, 5, Gate.Open);

            // Literal values (gate rule R17 compares them with bindings.ts TICKET_GATE).
            check((int)Gate.Open == 0 && (int)Gate.Off == 1 && (int)Gate.NoEfficiency == 2 &&
                  (int)Gate.NothingOpen == 3 && (int)Gate.NotKnownYet == 4, w, "TicketGate literal values 0-4");
        }

        private static void GateCase(Func<bool, string, string, bool> check, string w, int price, float efficiency,
                                     bool known, int openRuns, Gate expected)
        {
            Gate g = SkiMoneyRules.TicketGateOf(price, efficiency, known, openRuns);
            check(g == expected, w, $"TicketGateOf({price}, {efficiency}, {known}, {openRuns}) = {g}, expected {expected}");
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
            // A jump restarts counting partway through the month, as a load does.
            check(l.ThisMonthSinceLoad, w, "10: ThisMonthSinceLoad (counting restarted at the jump)");
            bool allZero = true;
            for (int i = 0; i < l.Count; i++)
                allZero &= IsZero(l.At(i).This) && IsZero(l.At(i).Last);
            check(allZero, w, "10: every Last and This zero");
            check(l.Advance(104) == Advance.Jumped, w, "10: a key that goes back is a jump");
            // 10b. the roll after a jump: that month was counted only from the jump, never a full month.
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
                   m.TicketsUnpaid == 0 && m.TicketsFree == 0 && m.TicketIncome == 0 && m.LiftCharged == 0.0 &&
                   m.TouristOnLeisure == 0;
        }

        // ── T9 the guest-day rules ───────────────────────────────────────────────────
        private static void GuestDayRules(Func<bool, string, string, bool> check)
        {
            const string w = "money T9";
            // TouristLeisureCounts(counted today, household day) → (counts, newGroup)
            LeisureCase(check, w, false, 0, true, true);
            LeisureCase(check, w, false, 2, true, false);
            LeisureCase(check, w, false, 1, false, false);
            LeisureCase(check, w, false, 3, false, false);
            LeisureCase(check, w, true, 0, false, false);
            LeisureCase(check, w, true, 2, false, false);
            // OutingPeople(party, already, household day) → (people, newGroup)
            OutingCase(check, w, 3, 0, 0, 3, true);      // 3 − 0
            OutingCase(check, w, 3, 1, 2, 2, false);     // 3 − 1
            OutingCase(check, w, 3, 3, 2, 0, false);
            OutingCase(check, w, 2, 0, 1, 2, false);
            OutingCase(check, w, 2, 2, 1, 0, false);
            OutingCase(check, w, 0, 0, 0, 0, false);
            OutingCase(check, w, 3, 5, 0, 0, false);     // 3 − clamp(5, 0, 3)
            OutingCase(check, w, 3, -1, 0, 3, true);     // 3 − 0
            OutingCase(check, w, -2, 0, 0, 0, false);    // max(0, −2)
        }

        private static void LeisureCase(Func<bool, string, string, bool> check, string w, bool today, byte hday, bool counts,
                                        bool newGroup)
        {
            bool c = SkiMoneyRules.TouristLeisureCounts(today, hday, out bool g);
            check(c == counts && g == newGroup, w,
                  $"TouristLeisureCounts({today}, {hday}) = ({c}, {g}), expected ({counts}, {newGroup})");
        }

        private static void OutingCase(Func<bool, string, string, bool> check, string w, int party, int already, byte hday,
                                       int people, bool newGroup)
        {
            int p = SkiMoneyRules.OutingPeople(party, already, hday, out bool g);
            check(p == people && g == newGroup, w,
                  $"OutingPeople({party}, {already}, {hday}) = ({p}, {g}), expected ({people}, {newGroup})");
        }

        // ── T10 the ledger's arrival flags ───────────────────────────────────────────
        private static void LedgerGuestDays(Func<bool, string, string, bool> check)
        {
            const string w = "money T10";
            var a = new EntityRef(412, 1);
            var l = new SkiResortLedger();
            check(l.Advance(200) == Advance.Started, w, "Advance(200) == Started");
            int rev0 = l.Revision;
            l.AddArrival(a, true, 1, true, true);
            l.AddArrival(a, true, 1, false, true);
            l.AddArrival(a, true, 1, false, false);
            l.AddArrival(a, true, 1, true, false);
            l.AddArrival(a, false, 1);
            l.AddArrival(a, false, 1, false, true);   // a resident: the flags are ignored
            check(l.TryGet(a, out SkiResortLedger.Entry e), w, "TryGet(A)");
            SkiResortLedger.Month m = e.This;
            check(m.TouristGuests == 4, w, "This.TouristGuests " + m.TouristGuests + ", expected 1+1+1+1 = 4");
            check(m.TouristParties == 2, w, "This.TouristParties " + m.TouristParties + ", expected 1+1 = 2 (newGroup only)");
            check(m.TouristOnLeisure == 2, w, "This.TouristOnLeisure " + m.TouristOnLeisure + ", expected 1+1 = 2 (onLeisure only)");
            check(m.LocalGuests == 2, w, "This.LocalGuests " + m.LocalGuests + ", expected 2");
            check(m.Guests == 6, w, "This.Guests " + m.Guests + ", expected 6");
            check(l.Revision == rev0 + 6, w, $"Revision {l.Revision}, expected {rev0} + 6");
            int rev1 = l.Revision;
            l.AddArrival(a, true, 0, true, true);
            check(l.Revision == rev1, w, "AddArrival(A, true, 0, true, true) leaves Revision unchanged");
            check(l.Advance(201) == Advance.Rolled, w, "Advance(201) == Rolled");
            l.TryGet(a, out e);
            check(e.Last.TouristOnLeisure == 2, w, "Last.TouristOnLeisure " + e.Last.TouristOnLeisure + ", expected 2");
            check(e.This.TouristOnLeisure == 0, w, "This.TouristOnLeisure " + e.This.TouristOnLeisure + ", expected 0");
            check(l.Total(true).TouristOnLeisure == 2, w, "Total(last).TouristOnLeisure " + l.Total(true).TouristOnLeisure);
        }

        // ── T11 a guest day: the counter's job logic on two stand-in sets ──────────────
        // Area S; household H {c1, c2, c3}, household K {k1}. The two helpers below repeat the scan jobs' branches
        // (SkiGuestCounterSystem LeisureScanJob / MeetingScanJob) with a HashSet and a Dictionary in
        // place of m_CitizensToday / m_HouseholdsToday; the ledger is the real one.
        private sealed class DaySets
        {
            public readonly HashSet<(int, int)> Citizens = new HashSet<(int, int)>();        // (citizen, sign)
            public readonly Dictionary<(int, int), byte> Households = new Dictionary<(int, int), byte>();  // (household, sign)
        }

        private static void LeisureArrival(DaySets d, SkiResortLedger l, EntityRef sign, int citizen, int household)
        {
            var ck = (citizen, sign.Index);
            var hk = (household, sign.Index);
            d.Households.TryGetValue(hk, out byte hday);
            if (!SkiMoneyRules.TouristLeisureCounts(d.Citizens.Contains(ck), hday, out bool newGroup))
                return;
            d.Citizens.Add(ck);
            d.Households[hk] = (byte)(hday | SkiMoneyRules.GuestDayLeisure);
            l.AddArrival(sign, true, 1, newGroup, true);
        }

        private static void OutingArrival(DaySets d, SkiResortLedger l, EntityRef sign, int household, int[] party)
        {
            var hk = (household, sign.Index);
            d.Households.TryGetValue(hk, out byte hday);
            int already = 0;
            for (int k = 0; k < party.Length; k++)
            {
                if (d.Citizens.Contains((party[k], sign.Index)))
                    already++;
            }
            int people = SkiMoneyRules.OutingPeople(party.Length, already, hday, out bool newGroup);
            for (int k = 0; k < party.Length; k++)
                d.Citizens.Add((party[k], sign.Index));
            d.Households[hk] = (byte)(hday | SkiMoneyRules.GuestDayOuting);
            if (people > 0)
                l.AddArrival(sign, true, people, newGroup, false);
        }

        private static void GuestDayScenario(Func<bool, string, string, bool> check)
        {
            const string w = "money T11";
            var s = new EntityRef(412, 1);
            const int h = 900, k = 901, c1 = 11, c2 = 12, c3 = 13, k1 = 21;
            var d = new DaySets();
            var l = new SkiResortLedger();
            l.Advance(200);

            LeisureArrival(d, l, s, c1, h);
            Step(check, w, l, s, 1, "c1 on leisure: counts, new group", 1, 1, 1);
            LeisureArrival(d, l, s, c1, h);
            Step(check, w, l, s, 2, "c1 leaves and returns: counted today, no", 1, 1, 1);
            LeisureArrival(d, l, s, c2, h);
            Step(check, w, l, s, 3, "c2 on leisure: counts, H already Leisure, not new", 2, 1, 2);
            OutingArrival(d, l, s, h, new[] { c1, c2, c3 });
            Step(check, w, l, s, 4, "outing H (c1, c2, c3): 2 already, people 1, not new", 3, 1, 2);
            OutingArrival(d, l, s, k, new[] { k1 });
            Step(check, w, l, s, 5, "outing K (k1): people 1, new group", 4, 2, 2);
            LeisureArrival(d, l, s, k1, k);
            Step(check, w, l, s, 6, "k1 on leisure: K came by outing, no", 4, 2, 2);
            d.Citizens.Clear();          // the day roll (AdvanceDay)
            d.Households.Clear();
            LeisureArrival(d, l, s, c1, h);
            Step(check, w, l, s, 7, "day roll, c1 on leisure: counts, new group", 5, 3, 3);
        }

        private static void Step(Func<bool, string, string, bool> check, string w, SkiResortLedger l, EntityRef sign, int step,
                                 string label, int tg, int tp, int tl)
        {
            l.TryGet(sign, out SkiResortLedger.Entry e);
            SkiResortLedger.Month m = e.This;
            check(m.TouristGuests == tg && m.TouristParties == tp && m.TouristOnLeisure == tl, w,
                  $"step {step} ({label}): TG/TP/TL {m.TouristGuests}/{m.TouristParties}/{m.TouristOnLeisure}, expected {tg}/{tp}/{tl}");
        }

        // ── T12 partial months ───────────────────────────────────────────────────────
        // A month of 262,144 frames (12 days a year): 95 % = 249,036.8 frames, so 249,037 is full and 249,036 short.
        private static void PartialMonths(Func<bool, string, string, bool> check)
        {
            const string w = "money T12";
            check(SkiMoneyRules.FullMonthShare == 0.95f, w, "FullMonthShare " + SkiMoneyRules.FullMonthShare);
            check(!SkiMoneyRules.IsShortMonth(249037u, 12), w, "IsShortMonth(249037, 12) should be false");
            check(SkiMoneyRules.IsShortMonth(249036u, 12), w, "IsShortMonth(249036, 12) should be true");
            check(!SkiMoneyRules.IsShortMonth(262144u, 12), w, "IsShortMonth(262144, 12) should be false");
            check(SkiMoneyRules.IsShortMonth(0u, 12), w, "IsShortMonth(0, 12) should be true");
            check(SkiMoneyRules.IsShortMonth(262144u, 24), w, "IsShortMonth(262144, 24) should be true (a month is 524,288 frames)");
            check(SkiMoneyRules.SimulatedPct(91750u, 12) == 34, w, "SimulatedPct(91750, 12) " + SkiMoneyRules.SimulatedPct(91750u, 12) + ", expected 34");
            check(SkiMoneyRules.SimulatedPct(262144u, 12) == 100, w, "SimulatedPct(262144, 12) " + SkiMoneyRules.SimulatedPct(262144u, 12));
            check(SkiMoneyRules.SimulatedPct(300000u, 12) == 100, w, "SimulatedPct(300000, 12) " + SkiMoneyRules.SimulatedPct(300000u, 12) + ", expected the 100 cap");
            check(SkiMoneyRules.SimulatedPct(0u, 12) == 0, w, "SimulatedPct(0, 12) " + SkiMoneyRules.SimulatedPct(0u, 12));

            // The ledger: a roll of a short month marks it partial (LastMonthSinceLoad); a start and a jump ignore it.
            var a = new EntityRef(412, 1);
            var l = new SkiResortLedger();
            check(l.Advance(100, true) == Advance.Started && l.ThisMonthSinceLoad && !l.LastMonthKnown, w,
                  "Advance(100, true) on a new ledger: Started, this month since load, no last month");
            check(l.Advance(100, true) == Advance.None && l.ThisMonthSinceLoad && !l.LastMonthKnown, w,
                  "Advance(100, true) again: None, flags unchanged");
            check(l.Advance(101, false) == Advance.Rolled && l.LastMonthKnown && l.LastMonthSinceLoad && !l.ThisMonthSinceLoad, w,
                  "Advance(101, false): Rolled, last month known and partial (since load)");
            l.AddArrival(a, false, 2);
            check(l.Advance(102, true) == Advance.Rolled && l.LastMonthKnown && l.LastMonthSinceLoad && !l.ThisMonthSinceLoad, w,
                  "Advance(102, true): Rolled, last month known and partial (short)");
            check(l.TryGet(a, out SkiResortLedger.Entry ea) && ea.Last.LocalGuests == 2 && ea.This.LocalGuests == 0, w,
                  $"a short roll still moves This to Last: Last {ea.Last.LocalGuests}, This {ea.This.LocalGuests}");
            check(l.Advance(103, false) == Advance.Rolled && l.LastMonthKnown && !l.LastMonthSinceLoad, w,
                  "Advance(103, false): Rolled, last month full");
            check(l.Advance(105, true) == Advance.Jumped && !l.LastMonthKnown && !l.LastMonthSinceLoad && l.ThisMonthSinceLoad, w,
                  "Advance(105, true): Jumped as before (a short flag does not change a jump)");

            // Advance(key) == Advance(key, false), result and flags, over one sequence with each kind of step.
            int[] keys = { 300, 300, 301, 302, 302, 305, 306, 306, 307, 309, 310 };
            var one = new SkiResortLedger();
            var two = new SkiResortLedger();
            bool same = true;
            for (int i = 0; i < keys.Length; i++)
            {
                Advance r1 = one.Advance(keys[i]);
                Advance r2 = two.Advance(keys[i], false);
                same &= r1 == r2 && one.MonthKey == two.MonthKey && one.ThisMonthSinceLoad == two.ThisMonthSinceLoad &&
                        one.LastMonthKnown == two.LastMonthKnown && one.LastMonthSinceLoad == two.LastMonthSinceLoad;
            }
            check(same, w, "Advance(key) differs from Advance(key, false) somewhere in the sequence");
        }

        // ── T13 a lift's ski area has a counted run ─────────────────────────────────────
        // LiftAreaRunOpen(linked, scoreKnown, writeHeld, runsCounted): unknown reads true (the full, old rate).
        private static void LiftAreaRuns(Func<bool, string, string, bool> check)
        {
            const string w = "money T13";
            check(SkiMoneyRules.LiftAreaRunOpen(false, false, false, 0), w, "LiftAreaRunOpen(not linked) should be true");
            check(SkiMoneyRules.LiftAreaRunOpen(true, false, false, 0), w, "LiftAreaRunOpen(linked, no score) should be true");
            check(SkiMoneyRules.LiftAreaRunOpen(true, true, true, 0), w, "LiftAreaRunOpen(linked, score held, 0 runs) should be true");
            check(!SkiMoneyRules.LiftAreaRunOpen(true, true, false, 0), w, "LiftAreaRunOpen(linked, released, 0 runs) should be false");
            check(SkiMoneyRules.LiftAreaRunOpen(true, true, false, 1), w, "LiftAreaRunOpen(linked, released, 1 run) should be true");
        }
    }
}
