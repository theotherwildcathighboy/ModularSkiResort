// PassRulesTests - tests for tests/ResortBatch: SkiPassRules (1.0.3 R3-B), the destination pass of tourist parties and the sale window of
// the residents' season pass. It is a `public static class PassRulesTests` with `Run(check)`. Program.Main calls PassRulesTests.Run(Check).
// Oracles are literal hand values; the fee, share, stepped price and phase tables use the same literals as the hub's mirror
// (mod/SkiResortUI/tools/test-hub-rules.js R3B.*). The hash distributions use 200,000 households and a +-0.01 band.
// Not testable headless (ECS): the ticket job, the hourly sale, the send. The bridge A/B covers them.

using System;
using System.Globalization;
using SkiResortMod.Systems;
using Phase = SkiResortMod.Systems.SkiPassRules.SalePhase;

namespace ResortBatch
{
    public static class PassRulesTests
    {
        public static void Run(Func<bool, string, string, bool> check)
        {
            Fees(check);
            Shares(check);
            Rolls(check);
            BuyHours(check);
            Phases(check);
            Prices(check);
        }

        private static string S(double v) => v.ToString("R", CultureInfo.InvariantCulture);

        private static void Fees(Func<bool, string, string, bool> check)
        {
            int[][] cases = { new[] { 320, 80, 80 }, new[] { 400, 80, 80 }, new[] { 240, 80, 60 }, new[] { 300, 80, 75 },
                              new[] { 20, 80, 5 }, new[] { 2000, 200, 200 }, new[] { 400, 0, 0 } };
            foreach (int[] c in cases)
            {
                int got = SkiPassRules.DestFee(c[0], c[1]);
                check(got == c[2], "R3B.1 DestFee(" + c[0] + ", " + c[1] + ")", "got " + got + ", want " + c[2]);
            }
            check(SkiPassRules.DestVisits == SkiMoneyRules.PassFullAt && SkiPassRules.DestVisits == 4, "R3B.1 DestVisits = PassFullAt = 4", "");
        }

        private static void Shares(Func<bool, string, string, bool> check)
        {
            // share % = round(DestShare × 100), the fee's price factor at the area's tolerance (SkiDayRules.PriceFactor)
            (int share, int pass, int ticket, int tol, int want)[] cases =
            {
                (50, 320, 80, 0, 40),    // PASS-3's year: fee 80, P 0.8
                (50, 240, 80, 0, 45),    // fee 60, P 0.9
                (50, 320, 80, 20, 45),   // a Chalet: P 0.9
                (0, 320, 80, 0, 0),      // passes off
                (50, 320, 0, 0, 0),      // tickets off
                (100, 400, 200, 40, 90), // fee 100 with a luxury lodge: P 0.9
            };
            foreach (var c in cases)
            {
                double f = SkiDayRules.PriceFactor(SkiPassRules.DestFee(c.pass, c.ticket), c.tol);
                double s = SkiPassRules.DestShare(c.share, c.pass, c.ticket, f);
                int pct = (int)Math.Round(s * 100.0, MidpointRounding.AwayFromZero);
                check(pct == c.want, "R3B.2 DestShare(" + c.share + ", " + c.pass + ", " + c.ticket + ", tol " + c.tol + ")", "got " + S(s) + ", want " + c.want + " %");
            }
            check(SkiPassRules.DestShare(50, 320, 80, double.NaN) == 0.0, "R3B.2 DestShare NaN factor = 0", "");
            check(SkiPassRules.DestShare(50, 2000, 80, 1.0) == 0.0, "R3B.2 DestShare no pass demand (2000 at 80) = 0", "");
            check(Math.Abs(SkiPassRules.DestShare(50, 320, 80, 0.8) - 0.4) < 1e-9, "R3B.2 DestShare exact 0.4", "");
        }

        private static void Rolls(Func<bool, string, string, bool> check)
        {
            const int n = 200000;
            const int season = 2027;
            bool det = true, range = true;
            int under = 0, bothUnder = 0, destUnderHalf = 0, otherSeasonSame = 0;
            for (int i = 0; i < n; i++)
            {
                double r = SkiPassRules.DestRoll(i, 1, season);
                det &= r == SkiPassRules.DestRoll(i, 1, season);
                range &= r >= 0.0 && r < 1.0;
                if (SkiPassRules.OnDestPass(i, 1, season, 0.4))
                    under++;
                if (r < 0.5)
                {
                    destUnderHalf++;
                    if (SkiPassRules.BuyPosition(i, season) < 0.25)
                        bothUnder++;
                }
                if ((r < 0.5) == (SkiPassRules.DestRoll(i, 1, season + 1) < 0.5))
                    otherSeasonSame++;
            }
            check(det, "R3B.10 DestRoll is deterministic", "");
            check(range, "R3B.10 DestRoll in [0, 1)", "");
            double frac = under / (double)n;
            check(Math.Abs(frac - 0.4) < 0.01, "R3B.10 OnDestPass at 0.4 holds for ~40 %", "got " + S(frac));
            // independent of the buy hour: among the households under 0.5, the buy CDF at 0.25 is still 2x - x^2 = 0.4375
            double cond = bothUnder / (double)destUnderHalf;
            check(Math.Abs(cond - 0.4375) < 0.01, "R3B.10 DestRoll independent of BuyPosition", "got " + S(cond));
            // a new season rolls anew: same side of 0.5 about half the time
            double same = otherSeasonSame / (double)n;
            check(Math.Abs(same - 0.5) < 0.01, "R3B.10 DestRoll differs by season", "got " + S(same));
            check(SkiPassRules.DestRoll(7, 1, season) != SkiPassRules.DestRoll(7, 2, season), "R3B.10 DestRoll differs by household version", "");
            check(!SkiPassRules.OnDestPass(7, 1, season, 0.0) && !SkiPassRules.OnDestPass(7, 1, season, double.NaN), "R3B.10 share 0 / NaN never", "");
        }

        private static void BuyHours(Func<bool, string, string, bool> check)
        {
            const int n = 200000;
            const int season = 2027;
            int m1 = 0, m2 = 0;
            bool range = true, all = true, none = true;
            for (int i = 0; i < n; i++)
            {
                double b = SkiPassRules.BuyPosition(i, season);
                range &= b >= 0.0 && b < 1.0;
                if (b < 0.25) m1++;
                if (b < 0.5) m2++;
                all &= SkiPassRules.HourCome(i, season, 1.0);
                none &= !SkiPassRules.HourCome(i, season, -0.01);
            }
            double f1 = m1 / (double)n, f2 = m2 / (double)n;
            check(range, "R3B.11 BuyPosition in [0, 1)", "");
            check(Math.Abs(f1 - 0.4375) < 0.01, "R3B.11 43.75 % buy in the first month", "got " + S(f1));
            check(Math.Abs(f2 - 0.75) < 0.01, "R3B.11 75 % buy before the step", "got " + S(f2));
            check(all, "R3B.11 every hour has come at the window's end", "");
            check(none, "R3B.11 no hour has come before the window", "");
        }

        private static void Phases(Func<bool, string, string, bool> check)
        {
            // mid-month dates (calendar month m = 0..11 at (m + 0.5) / 12), so float steps never sit on a boundary
            Phase[] july = { Phase.Closed, Phase.Closed, Phase.Closed, Phase.Closed, Phase.Closed, Phase.Closed,       // Jan-Jun
                             Phase.Before, Phase.Opening, Phase.Opening, Phase.Stepped, Phase.Stepped, Phase.Closed }; // Jul-Dec
            Phase[] january = { Phase.Before, Phase.Opening, Phase.Opening, Phase.Stepped, Phase.Stepped, Phase.Closed,
                                Phase.Closed, Phase.Closed, Phase.Closed, Phase.Closed, Phase.Closed, Phase.Closed };
            for (int m = 0; m < 12; m++)
            {
                float d = (m + 0.5f) / 12f;
                Phase pj = SkiPassRules.PhaseOf(SkiPassRules.SeasonPosition(d, 7));
                Phase pn = SkiPassRules.PhaseOf(SkiPassRules.SeasonPosition(d, 1));
                check(pj == july[m], "R3B.5 July season, month " + m, "got " + pj + ", want " + july[m]);
                check(pn == january[m], "R3B.6 January season (a June winter), month " + m, "got " + pn + ", want " + january[m]);
            }
            check(Math.Abs(SkiPassRules.SeasonPosition(7.5f / 12f, 7) - 1.5) < 1e-5, "R3B.5 SeasonPosition mid-August, July season = 1.5", "");
            check(Math.Abs(SkiPassRules.SeasonPosition(0.5f / 12f, 7) - 6.5) < 1e-5, "R3B.5 SeasonPosition mid-January, July season = 6.5", "");
            check(SkiPassRules.SeasonPosition(float.NaN, 1) == 0.0, "R3B.7 SeasonPosition NaN date = 0 (January season)", "");
            check(SkiPassRules.WindowPosition(1.0) == 0.0 && SkiPassRules.WindowPosition(5.0) == 1.0 && SkiPassRules.WindowPosition(3.0) == 0.5,
                "R3B.5 WindowPosition 1 -> 0, 3 -> 0.5, 5 -> 1", "");
            check(SkiPassRules.SaleOpen(Phase.Opening) && SkiPassRules.SaleOpen(Phase.Stepped) && !SkiPassRules.SaleOpen(Phase.Before)
                && !SkiPassRules.SaleOpen(Phase.Closed), "R3B.5 SaleOpen", "");
        }

        private static void Prices(Func<bool, string, string, bool> check)
        {
            int[][] stepped = { new[] { 320, 360 }, new[] { 400, 460 }, new[] { 100, 120 }, new[] { 20, 20 }, new[] { 2000, 2000 } };
            foreach (int[] c in stepped)
            {
                int got = SkiPassRules.SteppedPrice(c[0]);
                check(got == c[1], "R3B.3 SteppedPrice(" + c[0] + ")", "got " + got + ", want " + c[1]);
            }
            check(SkiPassRules.SalePrice(320, 80, Phase.Opening) == 320, "R3B.12 SalePrice opening = the setting", "");
            check(SkiPassRules.SalePrice(320, 80, Phase.Stepped) == 360, "R3B.12 SalePrice stepped = 360", "");
            check(SkiPassRules.SalePrice(320, 80, Phase.Before) == 0 && SkiPassRules.SalePrice(320, 80, Phase.Closed) == 0, "R3B.12 SalePrice outside = 0", "");
            check(SkiPassRules.SalePrice(320, 0, Phase.Opening) == 0, "R3B.12 SalePrice tickets off = 0", "");
        }
    }
}
