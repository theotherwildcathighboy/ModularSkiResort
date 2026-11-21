// The per-area price rules of 1.0.4 (T284): each ski area (Welcome Sign) has its own day ticket and its own season pass plan (an early
// price, a rise size, the months on sale and the months a rise starts, on a 12-month strip of the season). The persisted half is SkiPassPlan
// on the sign; this file is the pure half: the plan's sanitize, its phase / price / window per season position, the resolution of an area's
// prices (own values or the Options defaults, the plan locked at the season's sale start), the pass validity per area (SkiSeasonPassArea)
// and the steep "value left" pass demand of a buyer in the winter.
// Consumers: SkiLiftTicketSystem (the per-sign price cache, the lock, the ticket and sale jobs), SkiDaySystem (the send), SkiResortStatsSystem
// and the hub (hubRules.ts mirrors these numbers, gate HubRulesTests). Tests: tests/ResortBatch PlanRulesTests, tests/SaveBatch PassPlanTests.
// Pure file: Unity.Mathematics, SkiMoneyRules and SkiPassRules only. Every method is static, allocation-free and Burst-callable.
// The default plan (the Options price, +15 % from season month 3, on sale in season months 1-4) equals the 1.0.3 calendar of SkiPassRules
// at every season position; its demand before the winter equals SkiMoneyRules.PassFactor bit for bit (ResortBatch sweeps both).

using Unity.Mathematics;

namespace SkiResortMod.Systems
{
    public static class SkiPassPlanRules
    {
        public const int Months = SkiMoneyRules.MonthsPerYear;   // 12: season months 0..11 (0 = the season's first month, July)
        public const int MonthMask = (1 << Months) - 1;           // 0xFFF
        public const int RiseMaxPct = 50;
        public const int RiseStepPct = 5;
        public const int MaxRises = 3;
        /// <summary>The 1.0.3 calendar: on sale in season months 1-4 (August to November on a December winter).</summary>
        public const int DefaultSaleMask = 0x1E;
        /// <summary>The 1.0.3 calendar: one rise, from season month 3 (October).</summary>
        public const int DefaultRiseMask = 1 << SkiPassRules.StepMonth;
        public const int DefaultRisePct = SkiPassRules.StepPct;
        /// <summary>Value left: the winter counts as started no earlier than this season position (the end of the 1.0.3 window), so the
        /// default calendar never meets a winter share below 1 on a climate whose winter starts half a month early.</summary>
        public const double ValueLeftEarliest = 5.0;

        // SkiPassPlan.m_Flags bits
        public const byte FlagOwnTicket = 1;      // the area's ticket is its own (m_TicketPrice), else the Options default
        public const byte FlagOwnPlan = 2;        // the area's next plan is its own (the m_* plan fields), else the Options default plan
        public const byte FlagLockedDefault = 4;  // the lock copied the default plan (the area sells pool passes that season)

        // SkiSeasonPassArea.m_Flags bits
        public const byte AreaFlagPool = 1;       // sold by an area on the default plan: valid at every area on the default plan that season

        /// <summary>Never locked (SkiPassPlan.m_LockSeason).</summary>
        public const int NoLock = int.MinValue;

        public struct Plan
        {
            public int Early;      // the early price, 20-2,000 step 20
            public int RisePct;    // each rise, 0-50 step 5
            public int SaleMask;   // one unbroken run of season months
            public int RiseMask;   // inside the run, not its first month, at most 3

            public Plan(int early, int risePct, int saleMask, int riseMask)
            {
                Early = early;
                RisePct = risePct;
                SaleMask = saleMask;
                RiseMask = riseMask;
            }

            public bool SameAs(Plan o) => Early == o.Early && RisePct == o.RisePct && SaleMask == o.SaleMask && RiseMask == o.RiseMask;
        }

        /// <summary>The Options default plan: the clamped Options price and the 1.0.3 calendar.</summary>
        public static Plan DefaultPlan(int optionsPassPrice)
            => new Plan(SkiMoneyRules.ClampPassPrice(optionsPassPrice), DefaultRisePct, DefaultSaleMask, DefaultRiseMask);

        /// <summary>A rise size as stored: clamped to 0-50 and snapped to the nearest 5 (half up).</summary>
        public static int ClampRisePct(int pct)
            => math.min(RiseMaxPct, (math.clamp(pct, 0, RiseMaxPct) + RiseStepPct / 2) / RiseStepPct * RiseStepPct);

        /// <summary>The plan as the rules read it:
        /// - the early price clamped (SkiMoneyRules.ClampPassPrice) and the rise clamped (ClampRisePct);
        /// - the sale months filled from the first to the last set month (one unbroken run), or the default run when none is set;
        /// - the rise months kept only inside the run and not on its first month, the first MaxRises of them.</summary>
        public static Plan Sanitize(Plan p)
        {
            int sale = p.SaleMask & MonthMask;
            if (sale == 0)
                sale = DefaultSaleMask;
            int first = FirstBit(sale), last = LastBit(sale);
            int run = ((1 << (last + 1)) - 1) & ~((1 << first) - 1);
            int rise = p.RiseMask & run & ~(1 << first);
            int kept = 0, count = 0;
            for (int m = 0; m < Months && count < MaxRises; m++)
            {
                if ((rise & (1 << m)) != 0)
                {
                    kept |= 1 << m;
                    count++;
                }
            }
            return new Plan(SkiMoneyRules.ClampPassPrice(p.Early), ClampRisePct(p.RisePct), run, kept);
        }

        /// <summary>The lowest set bit of a non-zero mask (0..11), 0 for an empty mask.</summary>
        public static int FirstBit(int mask)
        {
            for (int m = 0; m < Months; m++)
                if ((mask & (1 << m)) != 0)
                    return m;
            return 0;
        }

        /// <summary>The highest set bit of a non-zero mask (0..11), 0 for an empty mask.</summary>
        public static int LastBit(int mask)
        {
            for (int m = Months - 1; m >= 0; m--)
                if ((mask & (1 << m)) != 0)
                    return m;
            return 0;
        }

        /// <summary>The number of set bits in the 12 months.</summary>
        public static int Count(int mask)
        {
            int n = 0;
            for (int m = 0; m < Months; m++)
                if ((mask & (1 << m)) != 0)
                    n++;
            return n;
        }

        /// <summary>The season month (0..11) of a season position (SkiPassRules.SeasonPosition); a non-finite position reads 0.</summary>
        public static int MonthOf(double seasonPosition)
            => math.isfinite(seasonPosition) ? math.clamp((int)math.floor(seasonPosition), 0, Months - 1) : 0;

        /// <summary>The sale phase at a season position for a sanitized plan: Before the run, Opening in it before the first rise, Stepped in it
        /// from the first rise on, Closed after it. The default plan gives SkiPassRules.PhaseOf at every position.</summary>
        public static SkiPassRules.SalePhase PhaseAt(Plan p, double seasonPosition)
        {
            int m = MonthOf(seasonPosition);
            int first = FirstBit(p.SaleMask), last = LastBit(p.SaleMask);
            if (m < first)
                return SkiPassRules.SalePhase.Before;
            if (m > last)
                return SkiPassRules.SalePhase.Closed;
            return RisesBy(p, m) > 0 ? SkiPassRules.SalePhase.Stepped : SkiPassRules.SalePhase.Opening;
        }

        /// <summary>The rises started at or before season month <paramref name="month"/>.</summary>
        public static int RisesBy(Plan p, int month)
        {
            int n = 0;
            for (int m = 0; m <= month && m < Months; m++)
                if ((p.RiseMask & (1 << m)) != 0)
                    n++;
            return n;
        }

        /// <summary>The price after <paramref name="rises"/> rises: the early price × (100 + rise) / 100 per rise, each rounded half up and
        /// snapped to the 20 step (SkiMoneyRules.ClampPassPrice), capped 2,000. One rise of 15 % is SkiPassRules.SteppedPrice.</summary>
        public static int PriceAfter(Plan p, int rises)
        {
            int price = SkiMoneyRules.ClampPassPrice(p.Early);
            for (int k = 0; k < rises; k++)
            {
                long v = (long)price * (100 + math.clamp(p.RisePct, 0, RiseMaxPct));
                price = SkiMoneyRules.ClampPassPrice((int)math.min((v + 50) / 100, (long)int.MaxValue));
            }
            return price;
        }

        /// <summary>The plan's price in season month <paramref name="month"/> (whether or not it is on sale).</summary>
        public static int PriceInMonth(Plan p, int month) => PriceAfter(p, RisesBy(p, month));

        /// <summary>The pass price a resident pays at this position: the month's price while on sale, 0 outside the run or with tickets off
        /// (a pass is judged against day tickets, SkiMoneyRules.SalePassPrice). The default plan gives SkiPassRules.SalePrice.</summary>
        public static int SalePrice(Plan p, int ticketPrice, double seasonPosition)
        {
            if (ticketPrice <= 0 || !SkiPassRules.SaleOpen(PhaseAt(p, seasonPosition)))
                return 0;
            return PriceInMonth(p, MonthOf(seasonPosition));
        }

        /// <summary>How far the run has gone, [0, 1) inside it: (position − first month) / run length. Below 0 before it, 1 or more after
        /// it. The default plan gives SkiPassRules.WindowPosition.</summary>
        public static double WindowPosition(Plan p, double seasonPosition)
        {
            int first = FirstBit(p.SaleMask);
            int len = math.max(1, LastBit(p.SaleMask) - first + 1);
            return (seasonPosition - first) / len;
        }

        /// <summary>The season's sale has started (open or already ended): the position is at or past the run's first month.</summary>
        public static bool SaleStarted(Plan p, double seasonPosition) => seasonPosition >= FirstBit(p.SaleMask);

        // ---- Resolution ----

        /// <summary>An area's prices for one season, as every consumer reads them.</summary>
        public struct Resolved
        {
            public int Ticket;          // the day ticket (0 = tickets off)
            public Plan Plan;           // the plan in force this season (sanitized)
            public Plan Next;           // the plan the next season gets as things stand (sanitized)
            public byte OnDefault;      // 1 = the plan in force is the default one: the area sells and honors pool passes this season
            public byte Locked;         // 1 = the plan in force is locked for this season (its sale started)
            public byte OwnTicket, OwnPlan;
        }

        /// <summary>Resolves an area. <paramref name="has"/> = the sign carries a SkiPassPlan (its fields follow). Without one, or with flags
        /// 0 and no lock for this season, it is exactly the Options default: the Options ticket and DefaultPlan(Options pass price).
        /// - Ticket: m_TicketPrice with FlagOwnTicket, else the Options ticket.
        /// - Plan in force: the locked plan when m_LockSeason == season (OnDefault = FlagLockedDefault), else the own plan with FlagOwnPlan,
        ///   else the default plan (OnDefault 1).
        /// - Next: the own plan with FlagOwnPlan, else the default plan.</summary>
        public static Resolved Resolve(bool has, byte flags, int ticketPrice, Plan own, int lockSeason, Plan locked, int season,
                                       int optionsTicket, int optionsPassPrice)
        {
            var r = new Resolved();
            bool ownTicket = has && (flags & FlagOwnTicket) != 0;
            bool ownPlan = has && (flags & FlagOwnPlan) != 0;
            r.OwnTicket = (byte)(ownTicket ? 1 : 0);
            r.OwnPlan = (byte)(ownPlan ? 1 : 0);
            r.Ticket = ownTicket ? SkiMoneyRules.ClampTicketPrice(ticketPrice) : SkiMoneyRules.ClampTicketPrice(optionsTicket);
            Plan def = DefaultPlan(optionsPassPrice);
            r.Next = ownPlan ? Sanitize(own) : def;
            if (has && lockSeason == season && lockSeason != NoLock)
            {
                r.Plan = Sanitize(locked);
                r.Locked = 1;
                r.OnDefault = (byte)((flags & FlagLockedDefault) != 0 ? 1 : 0);
            }
            else
            {
                r.Plan = r.Next;
                r.OnDefault = (byte)(ownPlan ? 0 : 1);
            }
            return r;
        }

        /// <summary>The lock is due: the area is not locked for <paramref name="season"/> and its plan in force has started selling.</summary>
        public static bool LockDue(in Resolved r, double seasonPosition) => r.Locked == 0 && SaleStarted(r.Plan, seasonPosition);

        // ---- Pass validity per area (SkiSeasonPassArea) ----

        /// <summary>A holder's pass is good at this area:
        /// - no SkiSeasonPassArea (a 1.0.3 standing): at every area (as 1.0.3);
        /// - a pool pass: at every area whose plan in force is the default one this season (<paramref name="areaOnDefault"/>);
        /// - an own-plan pass: only at the area that sold it (<paramref name="sameSign"/>; a bulldozed sign loads Null: nowhere).</summary>
        public static bool PassValidAt(bool hasArea, byte areaFlags, bool sameSign, bool areaOnDefault)
        {
            if (!hasArea)
                return true;
            if ((areaFlags & AreaFlagPool) != 0)
                return areaOnDefault;
            return sameSign;
        }

        // ---- Value left (steep pass demand in the winter) ----

        /// <summary>The climate winter in season positions: <paramref name="start"/> at or after ValueLeftEarliest, <paramref name="end"/>
        /// unwrapped after it (at most 12). Inputs are SkiClimateCalendar.WinterWindow's normalized year times and the season's first month.</summary>
        public static void WinterSpan(float winterStart, float winterEnd, int firstMonth, out double start, out double end)
        {
            double s = SkiPassRules.SeasonPosition(winterStart, firstMonth);
            double len = math.isfinite(winterEnd) && math.isfinite(winterStart) ? winterEnd - winterStart : 3.0 / 12.0;
            if (!(len > 0.0))
                len += 1.0;
            if (!(len > 0.0) || len > 1.0)
                len = 3.0 / 12.0;
            double e = s + len * Months;
            start = math.max(s, ValueLeftEarliest);
            end = math.min(e, (double)Months);
        }

        /// <summary>The share of the winter still ahead at a season position, in [0, 1]: 1 before the winter (and up to ValueLeftEarliest),
        /// linear to 0 at its end, 0 after it. A degenerate span (end at or before start) reads 1 before the start and 0 after it.
        /// A non-finite position reads 0.</summary>
        public static double ValueLeft(double seasonPosition, double winterStart, double winterEnd)
        {
            if (!math.isfinite(seasonPosition))
                return 0.0;
            if (seasonPosition < winterStart)
                return 1.0;
            if (!(winterEnd > winterStart) || seasonPosition >= winterEnd)
                return 0.0;
            return (winterEnd - seasonPosition) / (winterEnd - winterStart);
        }

        /// <summary>Pass demand with value left s: a buyer judges the price against the share of the winter still ahead (the price over s
        /// against the day ticket, SkiMoneyRules.PassFactor's line), and only that share of the buyers is left (× s). s = 1 is exactly
        /// PassFactor (1.0.3); s not &gt; 0 (NaN included) is 0. Computed in double: no int overflow as s goes to 0.
        /// Example at a 4-ticket price: s 0.8 → 0.70, 0.5 → 0.25, 0.25 → 0.</summary>
        public static double Demand(int passPrice, int ticketPrice, double valueLeft)
        {
            if (valueLeft >= 1.0)
                return SkiMoneyRules.PassFactor(passPrice, ticketPrice);
            if (!(valueLeft > 0.0) || ticketPrice <= 0 || passPrice <= 0)
                return 0.0;
            double ts = (double)ticketPrice * valueLeft;
            double f = ((SkiMoneyRules.PassFullAt + SkiMoneyRules.PassSpan) * ts - passPrice) / (SkiMoneyRules.PassSpan * ts);
            return math.clamp(f, 0.0, 1.0) * valueLeft;
        }
    }
}
