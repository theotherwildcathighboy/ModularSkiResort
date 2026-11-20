// PartyPriceTests - tests for tests/ResortBatch: the T258 price check of game-sent tourist parties (SkiDayRules.PartyPriceFactor,
// PartyPriceRoll, PartyStays, PartyWalksAway; 1.0.3). It is a `public static class PartyPriceTests` with `Run(check)`. Program.Main calls
// PartyPriceTests.Run(Check). Oracles are literal hand values; the curve table uses the same literals as the hub's mirror
// (mod/SkiResortUI/tools/test-hub-rules.js T258.*). The hash distributions use 200,000 households and a +-0.01 band.
// Not testable headless (ECS): the ticket job's order (the destination pass first, then this check) and the ledger roll in game.

using System;
using System.Globalization;
using SkiResortMod.Systems;

namespace ResortBatch
{
    public static class PartyPriceTests
    {
        public static void Run(Func<bool, string, string, bool> check)
        {
            Curve(check);
            DefaultPriceUnchanged(check);
            Rolls(check);
            Exemptions(check);
        }

        private static string S(double v) => v.ToString("R", CultureInfo.InvariantCulture);

        private static void Curve(Func<bool, string, string, bool> check)
        {
            // { price, tolerance, factor × 100 }
            int[][] cases =
            {
                new[] { 0, 0, 100 }, new[] { 80, 0, 100 }, new[] { 100, 0, 100 }, new[] { 120, 0, 90 }, new[] { 150, 0, 75 },
                new[] { 200, 0, 50 }, new[] { 260, 0, 50 }, new[] { 120, 20, 100 }, new[] { 200, 20, 60 }, new[] { 140, 40, 100 },
                new[] { 200, 40, 70 }, new[] { 200, -5, 50 },
            };
            foreach (int[] c in cases)
            {
                double got = SkiDayRules.PartyPriceFactor(c[0], c[1]);
                check(Math.Abs(got * 100 - c[2]) < 1e-9, "T258.1 PartyPriceFactor(" + c[0] + ", " + c[1] + ")", "got " + S(got) + ", want " + c[2] + " %");
            }
            // Gentler than the ski-day curve at every price and tolerance, and never rising with the price.
            bool gentler = true, falling = true;
            for (int tol = 0; tol <= 40; tol += 20)
            {
                double prev = 2.0;
                for (int p = 0; p <= 200; p += 5)
                {
                    double party = SkiDayRules.PartyPriceFactor(p, tol), day = SkiDayRules.PriceFactor(p, tol);
                    gentler &= party >= day;
                    falling &= party <= prev;
                    prev = party;
                }
            }
            check(gentler, "T258.2 the party curve is never below PriceFactor (0..200, tolerance 0/20/40)", "");
            check(falling, "T258.2 the party curve never rises with the price", "");
            check(SkiDayRules.PartyPriceFullAt - SkiDayRules.PriceSpan == SkiMoneyRules.TicketPriceDefault,
                  "T258.3 the full-demand price is the default ticket (100)", "");
            // Elasticity just above the default: (dP/dprice) × price / P = −(1/200) × 100 / 1 = −0.5 (stated design value).
            double e = (SkiDayRules.PartyPriceFactor(101, 0) - SkiDayRules.PartyPriceFactor(100, 0)) * 100.0 / 1.0;
            check(Math.Abs(e + 0.5) < 1e-9, "T258.3 elasticity just above 100 is -0.5", "got " + S(e));
        }

        private static void DefaultPriceUnchanged(Func<bool, string, string, bool> check)
        {
            // At or below the default ticket nobody walks away, for every household and day: today's numbers exactly (no roll is made).
            bool all = true;
            for (int h = 1; h <= 20000 && all; h++)
                for (int day = 0; day < 3; day++)
                {
                    all &= SkiDayRules.PartyStays(100, 0, h, 1, 7300 + day);
                    all &= SkiDayRules.PartyStays(80, 0, h, 1, 7300 + day);
                    all &= SkiDayRules.PartyStays(0, 0, h, 1, 7300 + day);
                    all &= SkiDayRules.PartyStays(140, 40, h, 1, 7300 + day);
                    all &= !SkiDayRules.PartyWalksAway(true, false, 100, 0, h, 1, 7300 + day);
                }
            check(all, "T258.4 at the default 100 (and 80, 0, 140 with tolerance 40) every party stays", "");
        }

        private static void Rolls(Func<bool, string, string, bool> check)
        {
            const int n = 200000;
            int stay200 = 0, stay150 = 0, bothLow = 0, sameDay = 0, nextDay = 0;
            for (int h = 1; h <= n; h++)
            {
                if (SkiDayRules.PartyStays(200, 0, h, 1, 7300)) stay200++;
                if (SkiDayRules.PartyStays(150, 0, h, 1, 7300)) stay150++;
                // Independent of the ski-day tourist roll (another salt).
                if (SkiDayRules.PartyPriceRoll(h, 1, 7300) < 0.5 && SkiDayRules.TouristRoll(h, 1, 7300) < 0.5) bothLow++;
                // Stateless: the same party and day give the same answer; the next day is a new roll.
                if (SkiDayRules.PartyStays(200, 0, h, 1, 7300) == SkiDayRules.PartyStays(200, 0, h, 1, 7300)) sameDay++;
                if (SkiDayRules.PartyStays(200, 0, h, 1, 7300) == SkiDayRules.PartyStays(200, 0, h, 1, 7301)) nextDay++;
            }
            double r200 = stay200 / (double)n, r150 = stay150 / (double)n, both = bothLow / (double)n, next = nextDay / (double)n;
            check(Math.Abs(r200 - 0.50) < 0.01, "T258.5 at 200 about half the parties stay", "got " + S(r200));
            check(Math.Abs(r150 - 0.75) < 0.01, "T258.5 at 150 about 75 % stay", "got " + S(r150));
            check(Math.Abs(both - 0.25) < 0.01, "T258.5 the price roll is independent of TouristRoll", "got " + S(both));
            check(sameDay == n, "T258.5 the same party and day give the same answer", "");
            check(Math.Abs(next - 0.5) < 0.01, "T258.5 the next day is a new roll (agreement about 50 % at a factor of 0.5)", "got " + S(next));
        }

        private static void Exemptions(Func<bool, string, string, bool> check)
        {
            bool pass = true, leisure = true;
            int outingAway = 0;
            for (int h = 1; h <= 20000; h++)
            {
                pass &= !SkiDayRules.PartyWalksAway(true, true, 200, 0, h, 1, 7300);       // on a destination pass: paid, never walks
                leisure &= !SkiDayRules.PartyWalksAway(false, false, 200, 0, h, 1, 7300);  // not an outing: the ski days priced it
                if (SkiDayRules.PartyWalksAway(true, false, 200, 0, h, 1, 7300)) outingAway++;
            }
            check(pass, "T258.6 a party on a destination pass never walks away (ticket 200)", "");
            check(leisure, "T258.6 a leisure arrival never walks away at the gate", "");
            check(Math.Abs(outingAway / 20000.0 - 0.5) < 0.015, "T258.6 an outing at 200 walks away about half the time", "got " + outingAway);
        }
    }
}
