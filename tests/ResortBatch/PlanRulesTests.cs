// PlanRulesTests - tests for tests/ResortBatch: SkiPassPlanRules (T284, 1.0.4), the per-area day ticket and season pass plan. It is a
// `public static class PlanRulesTests` with `Run(check)`. Program.Main calls PlanRulesTests.Run(Check).
// The key oracle: the default plan equals the 1.0.3 calendar of SkiPassRules (phase, price, window position) at every season position, and
// its demand before the winter equals SkiMoneyRules.PassFactor bit for bit. The other oracles are literal hand values; the hub's mirror
// (mod/SkiResortUI/tools/test-hub-rules.js T284.*) uses the same literals.
// Not testable headless (ECS): the lock on the sign, the ticket and sale jobs, the send. The bridge pass covers them.

using System;
using System.Globalization;
using SkiResortMod.Systems;
using Plan = SkiResortMod.Systems.SkiPassPlanRules.Plan;
using Phase = SkiResortMod.Systems.SkiPassRules.SalePhase;

namespace ResortBatch
{
    public static class PlanRulesTests
    {
        public static void Run(Func<bool, string, string, bool> check)
        {
            DefaultEqualsCalendar(check);
            Sanitize(check);
            CustomPlans(check);
            ValueLeftTable(check);
            DemandTable(check);
            Resolution(check);
            Validity(check);
        }

        private static string S(double v) => v.ToString("R", CultureInfo.InvariantCulture);

        private static void DefaultEqualsCalendar(Func<bool, string, string, bool> check)
        {
            bool phase = true, price = true, window = true, demand = true, started = true;
            string first = "";
            foreach (int setting in new[] { 20, 260, 400, 1000, 1980, 2000 })
            {
                Plan p = SkiPassPlanRules.Sanitize(SkiPassPlanRules.DefaultPlan(setting));
                foreach (int ticket in new[] { 0, 5, 80, 100, 200 })
                {
                    for (int k = 0; k < 12 * 64; k++)
                    {
                        double pos = k / 64.0;
                        Phase a = SkiPassPlanRules.PhaseAt(p, pos), b = SkiPassRules.PhaseOf(pos);
                        if (a != b) { phase = false; first = first == "" ? $"phase at {S(pos)}: {a} vs {b}" : first; }
                        int pa = SkiPassPlanRules.SalePrice(p, ticket, pos), pb = SkiPassRules.SalePrice(setting, ticket, b);
                        if (pa != pb) { price = false; first = first == "" ? $"price {setting}/{ticket} at {S(pos)}: {pa} vs {pb}" : first; }
                        if (SkiPassPlanRules.WindowPosition(p, pos) != SkiPassRules.WindowPosition(pos)) window = false;
                        // before the winter (December winter: position 5) value left is 1: the demand is PassFactor exactly
                        double vl = SkiPassPlanRules.ValueLeft(pos, 5.0, 8.0);
                        if (pos < 5.0 && SkiPassPlanRules.Demand(pa, ticket, vl) != SkiMoneyRules.PassFactor(pa, ticket)) demand = false;
                        if (SkiPassPlanRules.SaleStarted(p, pos) != (pos >= 1.0)) started = false;
                    }
                }
            }
            check(phase, "T284.1 default plan phase == SkiPassRules.PhaseOf over a sweep", first);
            check(price, "T284.1 default plan price == SkiPassRules.SalePrice over a sweep (settings x tickets)", first);
            check(window, "T284.1 default plan window position == SkiPassRules.WindowPosition", "");
            check(demand, "T284.1 default plan demand before the winter == SkiMoneyRules.PassFactor bit for bit", "");
            check(started, "T284.1 default plan sale starts at season month 1", "");
            check(SkiPassPlanRules.DefaultSaleMask == 0x1E && SkiPassPlanRules.DefaultRiseMask == 0x8 && SkiPassPlanRules.DefaultRisePct == 15,
                  "T284.1 default calendar constants (months 1-4, rise month 3, +15 %)", "");
            // A climate whose winter starts half a month early (season position 4.5): value left stays 1 through the default run.
            SkiPassPlanRules.WinterSpan(10.5f / 12f, 13.5f / 12f, 7, out double ws, out double we);
            check(Math.Abs(ws - 5.0) < 1e-6 && Math.Abs(we - 7.5) < 1e-4, "T284.1 WinterSpan early winter: start held at 5, end 7.5", $"got {S(ws)} .. {S(we)}");
            check(SkiPassPlanRules.ValueLeft(4.99, ws, we) == 1.0, "T284.1 early winter: value left 1 in the default run", "");
            SkiPassPlanRules.WinterSpan(11f / 12f, 14f / 12f, 7, out ws, out we);
            check(Math.Abs(ws - 5.0) < 1e-5 && Math.Abs(we - 8.0) < 1e-4, "T284.1 WinterSpan December winter: 5 .. 8", $"got {S(ws)} .. {S(we)}");
            // June winter (first month January): the winter is season months 5..8 as well
            SkiPassPlanRules.WinterSpan(5f / 12f, 8f / 12f, 1, out ws, out we);
            check(Math.Abs(ws - 5.0) < 1e-5 && Math.Abs(we - 8.0) < 1e-4, "T284.1 WinterSpan June winter: 5 .. 8", $"got {S(ws)} .. {S(we)}");
        }

        private static void Sanitize(Func<bool, string, string, bool> check)
        {
            Plan a = SkiPassPlanRules.Sanitize(new Plan(410, 17, 0b10010, 0b11111111));
            check(a.Early == 420 && a.RisePct == 15 && a.SaleMask == 0b11110 && a.RiseMask == 0b11100,
                  "T284.2 Sanitize: early snapped, rise snapped, gap filled, rises inside the run and not on its first month",
                  $"got {a.Early} {a.RisePct} 0x{a.SaleMask:X} 0x{a.RiseMask:X}");
            Plan b = SkiPassPlanRules.Sanitize(new Plan(5, 90, 0, 0));
            check(b.Early == 20 && b.RisePct == 50 && b.SaleMask == SkiPassPlanRules.DefaultSaleMask && b.RiseMask == 0,
                  "T284.2 Sanitize: early clamped up, rise capped at 50, an empty strip reads the default run", $"got {b.Early} {b.RisePct} 0x{b.SaleMask:X}");
            Plan c = SkiPassPlanRules.Sanitize(new Plan(400, 10, 0xFFF, 0xFFF));
            check(c.SaleMask == 0xFFF && c.RiseMask == 0b1110, "T284.2 Sanitize: at most 3 rises, the first three after the run's first month",
                  $"got 0x{c.RiseMask:X}");
            Plan d = SkiPassPlanRules.Sanitize(new Plan(99999, -5, 0x801 | 0x1000, 0));
            check(d.Early == 2000 && d.RisePct == 0 && d.SaleMask == 0xFFF, "T284.2 Sanitize: early capped, rise floored, first..last filled, bits past month 11 dropped", "");
            check(SkiPassPlanRules.ClampRisePct(12) == 10 && SkiPassPlanRules.ClampRisePct(13) == 15 && SkiPassPlanRules.ClampRisePct(48) == 50,
                  "T284.2 ClampRisePct snaps to 5 (half up)", "");
        }

        private static void CustomPlans(Func<bool, string, string, bool> check)
        {
            // July to January (season months 0-6), three rises of +20 %: Sep, Nov, Jan.
            Plan p = SkiPassPlanRules.Sanitize(new Plan(300, 20, 0x7F, (1 << 2) | (1 << 4) | (1 << 6)));
            int[] want = { 300, 300, 360, 360, 440, 440, 520, 520 };
            bool ok = true;
            string got = "";
            for (int m = 0; m < 8; m++)
            {
                int v = SkiPassPlanRules.PriceInMonth(p, m);
                got += v + " ";
                ok &= v == want[m];
            }
            check(ok, "T284.3 three rises of +20 % from 300: 300, 360, 440, 520 (each snapped to 20)", got);
            check(SkiPassPlanRules.PhaseAt(p, 0.5) == Phase.Opening, "T284.3 July in the run: Opening", "");
            check(SkiPassPlanRules.SalePrice(p, 100, 6.5) == 520 && SkiPassPlanRules.PhaseAt(p, 6.5) == Phase.Stepped, "T284.3 a run into the winter sells in January", "");
            check(SkiPassPlanRules.SalePrice(p, 100, 7.0) == 0 && SkiPassPlanRules.PhaseAt(p, 7.0) == Phase.Closed, "T284.3 after the run: Closed, no price", "");
            check(SkiPassPlanRules.SalePrice(p, 0, 1.0) == 0, "T284.3 tickets off: no pass sale", "");
            check(Math.Abs(SkiPassPlanRules.WindowPosition(p, 3.5) - 0.5) < 1e-12, "T284.3 window position over a 7-month run", "");
            Plan one = SkiPassPlanRules.Sanitize(new Plan(400, 15, 1 << 9, 0));
            check(SkiPassPlanRules.PhaseAt(one, 8.9) == Phase.Before && SkiPassPlanRules.PhaseAt(one, 9.2) == Phase.Opening &&
                  SkiPassPlanRules.PhaseAt(one, 10.0) == Phase.Closed && Math.Abs(SkiPassPlanRules.WindowPosition(one, 9.25) - 0.25) < 1e-12,
                  "T284.3 a 1-month run (April)", "");
            check(SkiPassPlanRules.PriceAfter(new Plan(2000, 50, 0x1E, 0), 3) == 2000, "T284.3 rises cap at 2,000", "");
            check(SkiPassPlanRules.PriceAfter(new Plan(400, 15, 0x1E, 0), 1) == SkiPassRules.SteppedPrice(400), "T284.3 one +15 % rise == SteppedPrice", "");
        }

        private static void ValueLeftTable(Func<bool, string, string, bool> check)
        {
            (double pos, double want)[] rows = { (0.0, 1.0), (4.99, 1.0), (5.0, 1.0), (5.75, 0.75), (6.5, 0.5), (7.25, 0.25), (8.0, 0.0), (11.0, 0.0) };
            foreach (var r in rows)
            {
                double v = SkiPassPlanRules.ValueLeft(r.pos, 5.0, 8.0);
                check(Math.Abs(v - r.want) < 1e-12, "T284.4 ValueLeft(" + S(r.pos) + ", 5, 8)", "got " + S(v) + ", want " + S(r.want));
            }
            check(SkiPassPlanRules.ValueLeft(double.NaN, 5.0, 8.0) == 0.0, "T284.4 ValueLeft NaN position = 0", "");
            check(SkiPassPlanRules.ValueLeft(4.0, 5.0, 5.0) == 1.0 && SkiPassPlanRules.ValueLeft(5.0, 5.0, 5.0) == 0.0, "T284.4 degenerate span", "");
            bool mono = true;
            double last = 2.0;
            for (int k = 0; k <= 1200; k++)
            {
                double v = SkiPassPlanRules.ValueLeft(k / 100.0, 5.0, 8.0);
                mono &= v <= last && v >= 0.0 && v <= 1.0;
                last = v;
            }
            check(mono, "T284.4 ValueLeft monotone non-increasing in [0, 1]", "");
        }

        private static void DemandTable(Func<bool, string, string, bool> check)
        {
            // a 4-ticket price (400 at 100): steep in the winter
            (double s, double want)[] rows = { (1.0, 1.0), (0.8, 0.7), (0.5, 0.25), (0.25, 0.0), (0.0, 0.0) };
            foreach (var r in rows)
            {
                double d = SkiPassPlanRules.Demand(400, 100, r.s);
                check(Math.Abs(d - r.want) < 1e-12, "T284.5 Demand(400, 100, " + S(r.s) + ")", "got " + S(d) + ", want " + S(r.want));
            }
            check(SkiPassPlanRules.Demand(400, 100, double.NaN) == 0.0 && SkiPassPlanRules.Demand(400, 0, 0.5) == 0.0 && SkiPassPlanRules.Demand(0, 100, 0.5) == 0.0,
                  "T284.5 Demand NaN / tickets off / no price = 0", "");
            check(SkiPassPlanRules.Demand(2000, 1, 1e-12) == 0.0 && SkiPassPlanRules.Demand(int.MaxValue, 200, 1e-300) == 0.0, "T284.5 Demand no overflow as s goes to 0", "");
            bool mono = true;
            for (int p = 20; p <= 2000; p += 20)
            {
                double prev = -1.0;
                for (int k = 0; k <= 100; k++)
                {
                    double d = SkiPassPlanRules.Demand(p, 100, k / 100.0);
                    mono &= d >= prev - 1e-15 && d >= 0.0 && d <= 1.0;
                    prev = d;
                }
            }
            check(mono, "T284.5 Demand non-decreasing in value left, in [0, 1]", "");
        }

        private static void Resolution(Func<bool, string, string, bool> check)
        {
            const int season = 2027;
            Plan own = new Plan(300, 20, 0x7F, 1 << 3);
            // no component == the Options default
            var r0 = SkiPassPlanRules.Resolve(false, 0, 0, default, SkiPassPlanRules.NoLock, default, season, 120, 400);
            check(r0.Ticket == 120 && r0.Plan.SameAs(SkiPassPlanRules.DefaultPlan(400)) && r0.OnDefault == 1 && r0.Locked == 0 && r0.Next.SameAs(r0.Plan),
                  "T284.6 Resolve: no component = the Options ticket and default plan", "");
            // flags 0, no lock, junk own fields == no component (critic C#2: never read HasComponent as "edited")
            var r1 = SkiPassPlanRules.Resolve(true, 0, 999, own, SkiPassPlanRules.NoLock, own, season, 120, 400);
            check(r1.Ticket == r0.Ticket && r1.Plan.SameAs(r0.Plan) && r1.OnDefault == 1 && r1.Locked == 0 && r1.OwnTicket == 0 && r1.OwnPlan == 0,
                  "T284.6 Resolve: a component with flags 0 behaves as no component (Options ticket, not m_TicketPrice)", "");
            bool same = true;
            for (int k = 0; k < 12 * 8; k++)
            {
                double pos = k / 8.0;
                same &= SkiPassPlanRules.SalePrice(r1.Plan, r1.Ticket, pos) == SkiPassPlanRules.SalePrice(r0.Plan, r0.Ticket, pos) &&
                        SkiPassPlanRules.LockDue(r1, pos) == SkiPassPlanRules.LockDue(r0, pos);
            }
            check(same, "T284.6 Resolve: flags 0 == no component at every season position", "");
            // own ticket only: the plan keeps following the default
            var r2 = SkiPassPlanRules.Resolve(true, SkiPassPlanRules.FlagOwnTicket, 155, own, SkiPassPlanRules.NoLock, default, season, 120, 400);
            check(r2.Ticket == 155 && r2.OnDefault == 1 && r2.Plan.SameAs(r0.Plan), "T284.6 Resolve: own ticket only", "");
            // own plan only: the Options ticket
            var r3 = SkiPassPlanRules.Resolve(true, SkiPassPlanRules.FlagOwnPlan, 155, own, SkiPassPlanRules.NoLock, default, season, 120, 400);
            check(r3.Ticket == 120 && r3.OnDefault == 0 && r3.Plan.SameAs(SkiPassPlanRules.Sanitize(own)), "T284.6 Resolve: own plan only", "");
            // locked this season (default copy) + an own plan edited after the sale started: this season keeps the lock, the next gets the edit
            Plan lockedDef = SkiPassPlanRules.DefaultPlan(400);
            byte fl = SkiPassPlanRules.FlagOwnPlan | SkiPassPlanRules.FlagLockedDefault;
            var r4 = SkiPassPlanRules.Resolve(true, fl, 0, own, season, lockedDef, season, 120, 200);
            check(r4.Locked == 1 && r4.OnDefault == 1 && r4.Plan.SameAs(lockedDef) && r4.Next.SameAs(SkiPassPlanRules.Sanitize(own)),
                  "T284.6 Resolve: an edit after the sale started applies from the next season (the lock stands, pool flag kept)", "");
            check(SkiPassPlanRules.SalePrice(r4.Plan, 120, 1.5) == 400, "T284.6 no cut within a season: the Options default lowered to 200 after the lock still sells at 400", "");
            var r5 = SkiPassPlanRules.Resolve(true, fl, 0, own, season, lockedDef, season + 1, 120, 200);
            check(r5.Locked == 0 && r5.OnDefault == 0 && r5.Plan.SameAs(SkiPassPlanRules.Sanitize(own)), "T284.6 Resolve: the next season gets the own plan", "");
            check(SkiPassPlanRules.LockDue(r0, 0.5) == false && SkiPassPlanRules.LockDue(r0, 1.0) && SkiPassPlanRules.LockDue(r0, 9.0) && !SkiPassPlanRules.LockDue(r4, 2.0),
                  "T284.6 LockDue: from the run's first month on (open or ended), never twice", "");
            // a lock never lowers the season's price below what it would have sold at without the edit
            bool never = true;
            foreach (int early in new[] { 20, 200, 400, 2000 })
            {
                Plan cut = new Plan(early, 0, 0x1E, 0);
                var rl = SkiPassPlanRules.Resolve(true, SkiPassPlanRules.FlagOwnPlan | SkiPassPlanRules.FlagLockedDefault, 0, cut, season, lockedDef, season, 120, 400);
                for (int k = 8; k < 40; k++)
                    never &= SkiPassPlanRules.SalePrice(rl.Plan, 120, k / 8.0) == SkiPassPlanRules.SalePrice(lockedDef, 120, k / 8.0);
            }
            check(never, "T284.6 a locked season sells at the locked plan whatever the next plan says", "");
        }

        private static void Validity(Func<bool, string, string, bool> check)
        {
            check(SkiPassPlanRules.PassValidAt(false, 0, false, false), "T284.7 a 1.0.3 standing (no area) is good at every area", "");
            check(SkiPassPlanRules.PassValidAt(true, 0, true, false), "T284.7 an own-plan pass is good at its area", "");
            check(!SkiPassPlanRules.PassValidAt(true, 0, false, true), "T284.7 an own-plan pass is not good at another area", "");
            check(!SkiPassPlanRules.PassValidAt(true, 0, false, false), "T284.7 a pass of a bulldozed area (Null sign) is good nowhere", "");
            check(SkiPassPlanRules.PassValidAt(true, SkiPassPlanRules.AreaFlagPool, false, true), "T284.7 a pool pass is good at another default-plan area", "");
            check(!SkiPassPlanRules.PassValidAt(true, SkiPassPlanRules.AreaFlagPool, true, false), "T284.7 a pool pass is not good at an own-plan area", "");
            // critic must-fix 1: the area edited after the sale started keeps OnDefault from its lock this season, so the pool pass stays good
            const int season = 2027;
            Plan lockedDef = SkiPassPlanRules.DefaultPlan(400);
            var edited = SkiPassPlanRules.Resolve(true, SkiPassPlanRules.FlagOwnPlan | SkiPassPlanRules.FlagLockedDefault, 0, new Plan(300, 0, 0x1E, 0),
                                                  season, lockedDef, season, 120, 400);
            check(SkiPassPlanRules.PassValidAt(true, SkiPassPlanRules.AreaFlagPool, false, edited.OnDefault != 0),
                  "T284.7 area edited after the sale started: a pool holder is still good there this season", "");
            var nextSeason = SkiPassPlanRules.Resolve(true, SkiPassPlanRules.FlagOwnPlan | SkiPassPlanRules.FlagLockedDefault, 0, new Plan(300, 0, 0x1E, 0),
                                                      season, lockedDef, season + 1, 120, 400);
            check(!SkiPassPlanRules.PassValidAt(true, SkiPassPlanRules.AreaFlagPool, false, nextSeason.OnDefault != 0),
                  "T284.7 ... and not next season (the area's own plan then)", "");
        }
    }
}
