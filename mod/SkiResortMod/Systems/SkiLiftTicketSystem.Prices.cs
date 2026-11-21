// Per-area prices (T284, 1.0.4): the one resolver of every ski area's day ticket and season pass plan. Step 2 of the ticket system's tick
// (main thread, before the gate watch and the sale) reads the SkiPassPlan of every live Welcome Sign (attractiveness CopySigns, Temp and
// Deleted skipped), resolves it against the Options defaults (SkiPassPlanRules.Resolve), LOCKS the plan in force when the season's sale has
// started (no price cuts within a season: a later edit or Options change is the next season's), and caches the area's numbers: ticket,
// sale price now, pass demand now (steep value left in the winter), window position, destination fee, lesson price, the pool flag.
// SkiDaySystem's area snapshot and SkiResortStatsSystem's per-area rows read this cache (AreaPriceOf); Revision moves when a value they
// use changes. The lock is the feature's only structural write on an untouched save: once per sign, a SkiPassPlan with flags 0 (follows
// the default), then SetComponentData once a season. Prices, calendar and demand stay the 1.0.3 values until the player edits an area.
// Engine notes: EngineNotes/SkiLiftTicketSystem.md "Per-area prices"

using System.Collections.Generic;
using Game.Common;
using Game.Tools;
using Unity.Entities;

namespace SkiResortMod.Systems
{
    public sealed partial class SkiLiftTicketSystem
    {
        /// <summary>One area's prices this tick.</summary>
        internal struct AreaPrice
        {
            public SkiPassPlanRules.Resolved R;
            public int Season;            // SkiMoneyRules.SeasonKeyOfDate
            public double SeasonPos;      // SkiPassRules.SeasonPosition
            public int FirstMonth;        // the season's first calendar month (1..12)
            public double WinterStart, WinterEnd;   // SkiPassPlanRules.WinterSpan, season positions
            public SkiPassRules.SalePhase Phase;
            public int SalePrice;         // the pass price now, 0 = not on sale (or tickets off)
            public double ValueLeft;      // SkiPassPlanRules.ValueLeft now
            public double Demand;         // SkiPassPlanRules.Demand(SalePrice, ticket, ValueLeft)
            public double WindowPos;      // SkiPassPlanRules.WindowPosition now
            public int DestFee;           // SkiPassRules.DestFee(the early price in force, ticket)
            public int LessonPrice;       // SkiMoneyRules.LessonPrice(multiplier, ticket)
        }

        private readonly Dictionary<Entity, AreaPrice> m_AreaPrices = new Dictionary<Entity, AreaPrice>();
        private readonly List<Entity> m_PriceSigns = new List<Entity>(4);
        private ulong m_PriceHash;
        private bool m_PriceHashKnown;

        /// <summary>Moves when any area's ticket, plan in force, pool flag, destination fee or lesson price changes (a hub edit, an Options edit,
        /// a lock, a season rollover). SkiDaySystem's snapshot key.</summary>
        internal int PriceRevision { get; private set; }

        private void PricesOnLoad()
        {
            m_AreaPrices.Clear();
            m_PriceSigns.Clear();
            m_PriceHashKnown = false;
            PriceRevision++;
        }

        /// <summary>The season clock of this tick: season key, position, first month and the winter span.</summary>
        private void SeasonClock(out int season, out double pos, out int firstMonth, out double winterStart, out double winterEnd)
        {
            SkiClimateCalendar.WinterWindow(m_ClimateSystem, m_PrefabSystem, out float ws, out float we);
            firstMonth = SkiMoneyRules.SeasonFirstMonthOf(ws);
            pos = SkiPassRules.SeasonPosition(m_TimeSystem.normalizedDate, firstMonth);
            season = SkiMoneyRules.SeasonKeyOfDate(m_TimeSystem.year, m_TimeSystem.normalizedDate, firstMonth);
            SkiPassPlanRules.WinterSpan(ws, we, firstMonth, out winterStart, out winterEnd);
        }

        /// <summary>Step 2 (main thread): resolve, lock and cache every live sign's prices. Bumps PriceRevision on a change.</summary>
        private void RefreshAreaPrices()
        {
            SeasonClock(out int season, out double pos, out int firstMonth, out double ws, out double we);
            m_Attractiveness.CopySigns(m_PriceSigns);
            m_AreaPrices.Clear();
            ulong h = 1469598103934665603UL;
            for (int i = 0; i < m_PriceSigns.Count; i++)
            {
                Entity sign = m_PriceSigns[i];
                if (sign == Entity.Null || !EntityManager.Exists(sign) || EntityManager.HasComponent<Temp>(sign) ||
                    EntityManager.HasComponent<Deleted>(sign) || m_AreaPrices.ContainsKey(sign))
                    continue;
                AreaPrice ap = ResolveArea(sign, season, pos, firstMonth, ws, we, lockNow: true);
                m_AreaPrices[sign] = ap;
                h = HashMix(h, (ulong)(uint)sign.Index);
                h = HashMix(h, (ulong)(uint)sign.Version);
                h = HashMix(h, (ulong)(uint)ap.R.Ticket);
                h = HashMix(h, (ulong)(uint)ap.R.Plan.Early);
                h = HashMix(h, (ulong)(uint)ap.R.Plan.RisePct);
                h = HashMix(h, (ulong)(uint)ap.R.Plan.SaleMask);
                h = HashMix(h, (ulong)(uint)ap.R.Plan.RiseMask);
                h = HashMix(h, (ulong)ap.R.OnDefault);
                h = HashMix(h, (ulong)(uint)ap.DestFee);
                h = HashMix(h, (ulong)(uint)ap.LessonPrice);
                h = HashMix(h, (ulong)(uint)ap.Season);
            }
            h = HashMix(h, (ulong)(uint)PassShare);
            if (!m_PriceHashKnown || h != m_PriceHash)
            {
                m_PriceHashKnown = true;
                m_PriceHash = h;
                PriceRevision++;
            }
        }

        private static ulong HashMix(ulong h, ulong v)
        {
            unchecked
            {
                h ^= v;
                h *= 1099511628211UL;
            }
            return h;
        }

        /// <summary>The area's prices: this tick's cache entry, else resolved now without a lock (a sign the cache has not seen yet, the
        /// editor, a system that reads before the first tick). Main thread.</summary>
        internal AreaPrice AreaPriceOf(Entity sign)
        {
            if (m_AreaPrices.TryGetValue(sign, out AreaPrice ap))
                return ap;
            SeasonClock(out int season, out double pos, out int firstMonth, out double ws, out double we);
            return ResolveArea(sign, season, pos, firstMonth, ws, we, lockNow: false);
        }

        /// <summary>The Options defaults as an area without its own prices reads them (the clamped settings, live).</summary>
        internal AreaPrice DefaultAreaPrice()
        {
            SeasonClock(out int season, out double pos, out int firstMonth, out double ws, out double we);
            return Compose(SkiPassPlanRules.Resolve(false, 0, 0, default, SkiPassPlanRules.NoLock, default, season, ReadPrice(), ReadPassPrice()),
                           season, pos, firstMonth, ws, we);
        }

        private AreaPrice ResolveArea(Entity sign, int season, double pos, int firstMonth, double ws, double we, bool lockNow)
        {
            bool has = sign != Entity.Null && EntityManager.Exists(sign) && EntityManager.HasComponent<SkiPassPlan>(sign);
            SkiPassPlan c = has ? EntityManager.GetComponentData<SkiPassPlan>(sign) : SkiPassPlan.FollowDefault();
            int optTicket = ReadPrice(), optPass = ReadPassPrice();
            SkiPassPlanRules.Resolved r = SkiPassPlanRules.Resolve(has, c.m_Flags, c.m_TicketPrice, c.Own, c.m_LockSeason, c.LockedPlan, season,
                                                                   optTicket, optPass);
            if (lockNow && SkiPassPlanRules.LockDue(r, pos))
            {
                // The season's sale has started: its plan stays as it is now (no cuts within a season).
                c.SetLocked(season, r.Plan);
                c.m_Flags = (byte)(r.OnDefault != 0 ? c.m_Flags | SkiPassPlanRules.FlagLockedDefault
                                                    : c.m_Flags & ~SkiPassPlanRules.FlagLockedDefault);
                if (has)
                    EntityManager.SetComponentData(sign, c);
                else
                    EntityManager.AddComponentData(sign, c);   // structural: once per sign
                has = true;
                r = SkiPassPlanRules.Resolve(true, c.m_Flags, c.m_TicketPrice, c.Own, c.m_LockSeason, c.LockedPlan, season, optTicket, optPass);
                Mod.Log.Info($"Passes [plan] ski area {Fmt(sign)} season {season}: the sale started, plan locked for the season: {PlanText(r.Plan)}" +
                             (r.OnDefault != 0 ? " (the Options default: pool passes, good at every area on the default plan)" : " (the area's own plan: its passes are good here only)") +
                             "; a later edit applies from the next season");
            }
            return Compose(r, season, pos, firstMonth, ws, we);
        }

        private AreaPrice Compose(in SkiPassPlanRules.Resolved r, int season, double pos, int firstMonth, double ws, double we)
        {
            var ap = new AreaPrice
            {
                R = r,
                Season = season,
                SeasonPos = pos,
                FirstMonth = firstMonth,
                WinterStart = ws,
                WinterEnd = we,
                Phase = SkiPassPlanRules.PhaseAt(r.Plan, pos),
                SalePrice = SkiPassPlanRules.SalePrice(r.Plan, r.Ticket, pos),
                ValueLeft = SkiPassPlanRules.ValueLeft(pos, ws, we),
                WindowPos = SkiPassPlanRules.WindowPosition(r.Plan, pos),
                DestFee = SkiPassRules.DestFee(r.Plan.Early, r.Ticket),
                LessonPrice = SkiMoneyRules.LessonPrice(LessonMultiplier, r.Ticket),
            };
            ap.Demand = SkiPassPlanRules.Demand(ap.SalePrice, r.Ticket, ap.ValueLeft);
            return ap;
        }

        internal enum AreaEdit : byte { Ticket = 0, TicketDefault = 1, PassEarly = 2, PassRise = 3, PassMonths = 4, PlanDefault = 5 }

        /// <summary>A hub edit of one area's prices (main thread, SkiResortHubUISystem). Locks the season's plan first when its sale has
        /// started (so the edit is the next season's), seeds the own plan from the plan the next season would get, applies the edit
        /// (sanitized), writes the SkiPassPlan and refreshes the area's cache entry. Returns the log text, or null when the sign is gone.
        /// Ticket: <paramref name="value"/> = the price (snapped, SkiMoneyRules.SnapTicketPrice). PassEarly: the early price (ClampPassPrice).
        /// PassRise: the rise % (ClampRisePct). PassMonths: sale mask | rise mask &lt;&lt; 12. TicketDefault / PlanDefault: follow the Options
        /// default again (the ticket at once, the plan from the season whose sale has not started).</summary>
        internal string EditArea(Entity sign, AreaEdit edit, int value)
        {
            if (sign == Entity.Null || !EntityManager.Exists(sign) || EntityManager.HasComponent<Deleted>(sign))
                return null;
            SeasonClock(out int season, out double pos, out int firstMonth, out double ws, out double we);
            bool has = EntityManager.HasComponent<SkiPassPlan>(sign);
            SkiPassPlan c = has ? EntityManager.GetComponentData<SkiPassPlan>(sign) : SkiPassPlan.FollowDefault();
            int optTicket = ReadPrice(), optPass = ReadPassPrice();
            SkiPassPlanRules.Resolved r = SkiPassPlanRules.Resolve(has, c.m_Flags, c.m_TicketPrice, c.Own, c.m_LockSeason, c.LockedPlan, season,
                                                                   optTicket, optPass);
            string locked = "";
            if (SkiPassPlanRules.LockDue(r, pos))
            {
                c.SetLocked(season, r.Plan);
                c.m_Flags = (byte)(r.OnDefault != 0 ? c.m_Flags | SkiPassPlanRules.FlagLockedDefault
                                                    : c.m_Flags & ~SkiPassPlanRules.FlagLockedDefault);
                locked = $"; season {season} locked first at {PlanText(r.Plan)}";
            }
            SkiPassPlanRules.Plan own = (c.m_Flags & SkiPassPlanRules.FlagOwnPlan) != 0 ? SkiPassPlanRules.Sanitize(c.Own) : r.Next;
            string what;
            switch (edit)
            {
                case AreaEdit.Ticket:
                    c.m_TicketPrice = SkiMoneyRules.SnapTicketPrice(value);
                    c.m_Flags |= SkiPassPlanRules.FlagOwnTicket;
                    what = $"lift ticket {r.Ticket} -> {c.m_TicketPrice} (the area's own)";
                    break;
                case AreaEdit.TicketDefault:
                    c.m_Flags = (byte)(c.m_Flags & ~SkiPassPlanRules.FlagOwnTicket);
                    what = $"lift ticket {r.Ticket} -> the Options default {optTicket}";
                    break;
                case AreaEdit.PassEarly:
                case AreaEdit.PassRise:
                case AreaEdit.PassMonths:
                    if (edit == AreaEdit.PassEarly)
                        own.Early = value;
                    else if (edit == AreaEdit.PassRise)
                        own.RisePct = value;
                    else
                    {
                        own.SaleMask = value & SkiPassPlanRules.MonthMask;
                        own.RiseMask = (value >> SkiPassPlanRules.Months) & SkiPassPlanRules.MonthMask;
                    }
                    own = SkiPassPlanRules.Sanitize(own);
                    c.SetOwn(own);
                    c.m_Flags |= SkiPassPlanRules.FlagOwnPlan;
                    what = $"pass plan -> {PlanText(own)} (the area's own)";
                    break;
                case AreaEdit.PlanDefault:
                    c.m_Flags = (byte)(c.m_Flags & ~SkiPassPlanRules.FlagOwnPlan);
                    what = $"pass plan -> the Options default ({PlanText(SkiPassPlanRules.DefaultPlan(optPass))})";
                    break;
                default:
                    return null;
            }
            if (has)
                EntityManager.SetComponentData(sign, c);
            else
                EntityManager.AddComponentData(sign, c);   // structural: once per sign
            SkiPassPlanRules.Resolved now = SkiPassPlanRules.Resolve(true, c.m_Flags, c.m_TicketPrice, c.Own, c.m_LockSeason, c.LockedPlan, season,
                                                                     optTicket, optPass);
            m_AreaPrices[sign] = Compose(now, season, pos, firstMonth, ws, we);
            string when = now.Locked != 0 && edit != AreaEdit.Ticket && edit != AreaEdit.TicketDefault
                ? $"; this season keeps {PlanText(now.Plan)} (its sale started), the edit applies from the next season"
                : "";
            return $"ski area {Fmt(sign)}: {what}{locked}{when}";
        }

        /// <summary>"early 400, +15 % in season month(s) 3, on sale in season months 1-4".</summary>
        internal static string PlanText(SkiPassPlanRules.Plan p)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("early ").Append(p.Early).Append(", ");
            if (p.RiseMask == 0 || p.RisePct == 0)
                sb.Append("no rise");
            else
            {
                sb.Append('+').Append(p.RisePct).Append(" % in season month(s)");
                for (int m = 0; m < SkiPassPlanRules.Months; m++)
                    if ((p.RiseMask & (1 << m)) != 0)
                        sb.Append(' ').Append(m);
            }
            sb.Append(", on sale in season months ").Append(SkiPassPlanRules.FirstBit(p.SaleMask)).Append('-').Append(SkiPassPlanRules.LastBit(p.SaleMask));
            return sb.ToString();
        }
    }
}
