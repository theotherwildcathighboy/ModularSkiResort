// SkiResortLedger — the PURE monthly guest and money ledger of Round E1 (docs/research/econ-round/spec/
// ch4-money.md §4.6, §6.2; decisions D25 guests, D26 tickets, D23 lift charge). In memory only, reset on
// every load (nothing is serialized); one Entry per ski area keyed by its sign as an EntityRef, with
// EntityRef.Null = Not linked (lift charges only). Consumers: SkiGuestCounterSystem (owner of the one
// instance, Advance / AddArrival / Prune), SkiLiftTicketSystem (AddTickets), SkiLiftChargeSystem
// (AddLiftCharge), SkiResortStatsSystem (Entry.This / Last and the month flags), tests/ResortBatch
// MoneyBatch (T7; T10-T11 Round S1).
//
// CONTRACT (ch4 §4.6, §6.2):
//   * Storage: a Dictionary<EntityRef, int> index into a List<Entry>. Every Add* creates the entry on
//     first use and increments Revision; a call whose amounts are all zero changes nothing; inputs
//     below 0 (and a non-finite or non-positive lift amount) are ignored — the ledger never subtracts.
//     No allocation once the list and the index have grown to the ski-area count.
//   * Advance(key): Started (first call after Reset: MonthKey = key, ThisMonthSinceLoad), None (same
//     key), Rolled (key == MonthKey + 1: every Last = This, This = 0; LastMonthKnown;
//     LastMonthSinceLoad = ThisMonthSinceLoad; ThisMonthSinceLoad = false), Jumped (any other key:
//     every Last and This = 0; LastMonthKnown = false; LastMonthSinceLoad = false, since there is no last
//     month to describe; ThisMonthSinceLoad = TRUE — E1 fixer, e1-spec §8.9 F8: counting restarts partway
//     through the month as after a load, so the next roll never calls that month full). "Since load" thus
//     reads "since counting started (load or jump)"; since Round M1 a roll can also set LastMonthSinceLoad for a
//     short month (Advance(key, closedMonthShort), below). Only SkiGuestCounterSystem calls it (every 16 sim
//     frames); it also keeps the sim frame counting started at (CountingSinceFrame) for the hub's projection.
//   * Prune(live): drops the entries whose sign is not in <live>; EntityRef.Null is always kept.
//   * Revision increments on every change (Reset, Started / Rolled / Jumped, any Add* that changed a
//     number, a Prune that removed an entry); readers compare it for equality.
//
// Round E1 (2026-10-04): stage 0 (S0) laid down the exact §6.2 signatures; stream S4 wrote the bodies.
//
// ROUND S1 D23 (ski-days spec ch3 §1.5; stream C, 2026-10-05): AddArrival gains newGroup (default true) and
// onLeisure (default false), so every E1 call reads as before. Month.TouristOnLeisure = tourists counted through
// the leisure scan, a subset of TouristGuests. Meaning change, no rename: TouristParties is now GROUP-DAYS —
// distinct households per ski area per game day, summed over the month (= distinct households of the month at
// the default 12 days a year, one month = one day): the counter passes newGroup only for a household's first
// count there that day. TouristOnLeisure can be slightly high: an outing member already on site with a Leisure
// aimed at the lodge is counted by the leisure scan before its outing turns Attending (the outing then counts
// only the rest, so TouristGuests stays right).
//
// ROUND M1 (D5, docs/research/ski-days/measure/m1-spec.md M7c; Builder B, 2026-10-05): Advance(key, closedMonthShort)
// — the existing Advance(key) delegates with false, so every caller and test reads as before. A roll whose closed
// month was short (fewer than SkiMoneyRules.FullMonthShare of its frames simulated: a developer time jump that lands
// exactly one month later rolls, it does not jump) sets LastMonthSinceLoad. Meaning change, no rename:
// LastMonthSinceLoad now reads "last month PARTIAL: since load, since a jump, or too few frames simulated". The hub
// needs no change (SkiResortStatsSystem.cs:1714, lastMonthFull = LastMonthKnown && !LastMonthSinceLoad): the
// Guests view marks such a month "(partial)" and the Money tile does not use it as a full month.
//
// CATALOG PHASE B (2026-10-11, economy group; docs/research/art/assets-2026-10/code-round-2/round-2-contract.md §7.4
// D15, seam S8): the optional lodge spend. Month gains LodgeSpends (guests who spent at a lodge after their ticket) and
// LodgeSpendIncome (₵ moved to the city with them); AddLodgeSpend(sign, spends, income) books one batch under the
// AddTickets rules (inputs below 0 read 0, an all-zero call changes nothing, Revision++ otherwise). Advance / Prune / Total
// carry the two fields like the ticket fields (the whole Month moves). A setting of 0 never calls it with a
// non-zero amount, so a city that turns the option off has every month bit-identical to Phase A's (ResortBatch
// LadderTests ledger rows; MoneyBatch T7 / T10-T11 unchanged). Consumers: SkiLiftTicketSystem (books it),
// SkiResortStatsSystem (the hub's month income, lift group).
//
// SEASON PASSES (DECISIONS 240, docs/research/econ-2/build/season-pass-v1.md): Month gains PassesSold,
// PassIncome (₵ paid for them) and PassHolderGuests (guests who came in on a pass); AddPasses(sign, sold, income,
// holderGuests = 0, seasonKey = NoSeason) books a batch under the AddTickets rules; Advance / Total carry the fields (the whole Month moves).
// Hub: Entry.PassSeasonKey / PassSeasonSold / PassSeasonIncome keep the ski season's sales across month rolls (PassSeasonSoldOf / IncomeOf).
// Consumers: SkiLiftTicketSystem (books), SkiGuestCounterSystem (month lines), SkiResortStatsSystem (the hub's rows).
//
// LESSONS AND THE PRE-SEASON SALE (DECISIONS 241, docs/research/econ-2/build/round-2.md): Month gains Lessons / LessonIncome
// (AddLessons) and PassesPre / PassIncomePre; AddPasses(..., preSeason = true) books the pre-season sale as a SUBSET of
// PassesSold / PassIncome (the totals still count every pass once), also kept per season on the Entry (PassSeasonPre /
// PassSeasonPreIncome; PassSeasonPreOf / PassSeasonPreIncomeOf). Advance / Total carry the month fields.
//
// PURE FILE: `using System;` and `using System.Collections.Generic;` plus EntityRef only (tests/ResortBatch
// links it).

using System;
using System.Collections.Generic;

namespace SkiResortMod.Systems
{
    public sealed class SkiResortLedger
    {
        public struct Month
        {
            public int TouristGuests, TouristParties, LocalGuests;
            public int TouristOnLeisure;    // Round S1 D23: tourists counted through the leisure scan (⊂ TouristGuests)
            public int TicketsPaid, TicketsUnpaid, TicketsFree;
            public long TicketIncome;
            public double LiftCharged;
            // Catalog Phase B (D15, seam S8): the optional lodge spend (0 while the setting is 0).
            public int LodgeSpends;          // paying guests who also spent at the lodge they arrived at
            public long LodgeSpendIncome;    // ₵ moved from their households to the city for it
            // Passes sold at this ski area's gate, what they paid, and the guests who came in on a pass (a holder's arrival:
            // not Paid, not Unpaid, not Free). All 0 while passes are off.
            public int PassesSold;
            public long PassIncome;
            public int PassHolderGuests;
            // PassesPre / PassIncomePre = the part of PassesSold / PassIncome sold in the pre-season sale;
            // Lessons / LessonIncome = lessons taken (one per person) and what they paid.
            public int PassesPre;
            public long PassIncomePre;
            public int Lessons;
            public long LessonIncome;
            public int Guests => TouristGuests + LocalGuests;
        }

        // Hub: the pass sales of the ski season in PassSeasonKey (SkiMoneyRules.SeasonKeyOf), kept across month rolls
        // (Advance never touches them); a reader asks PassSeasonSold / PassSeasonIncome for the season it wants and gets 0 for another.
        // PassSeasonPre / PassSeasonPreIncome = the pre-season sale's part of those totals (a subset, never added twice).
        public struct Entry
        {
            public EntityRef Sign; public Month This, Last; public int PassSeasonKey, PassSeasonSold; public long PassSeasonIncome;
            public int PassSeasonPre; public long PassSeasonPreIncome;
        }

        public enum AdvanceResult : byte { None = 0, Started = 1, Rolled = 2, Jumped = 3 }

        public const int NoMonth = int.MinValue;
        public const int NoSeason = int.MinValue;   // AddPasses without a season: the season totals are left alone

        /// <summary>Hub: passes sold at this ski area in <paramref name="seasonKey"/> (0 for any other season).</summary>
        public static int PassSeasonSoldOf(in Entry e, int seasonKey) => e.PassSeasonKey == seasonKey ? e.PassSeasonSold : 0;

        /// <summary>Hub: what those passes paid (0 for any other season).</summary>
        public static long PassSeasonIncomeOf(in Entry e, int seasonKey) => e.PassSeasonKey == seasonKey ? e.PassSeasonIncome : 0L;

        /// <summary>Hub: the passes of <paramref name="seasonKey"/> that were sold in the pre-season sale (a subset of PassSeasonSoldOf).</summary>
        public static int PassSeasonPreOf(in Entry e, int seasonKey) => e.PassSeasonKey == seasonKey ? e.PassSeasonPre : 0;

        /// <summary>Hub: what those pre-season passes paid (a subset of PassSeasonIncomeOf).</summary>
        public static long PassSeasonPreIncomeOf(in Entry e, int seasonKey) => e.PassSeasonKey == seasonKey ? e.PassSeasonPreIncome : 0L;
        public int MonthKey { get; private set; } = NoMonth;
        public bool ThisMonthSinceLoad { get; private set; }
        public bool LastMonthKnown { get; private set; }
        /// <summary>Last month partial (Round M1 meaning): counting started in it (a load or a jump), or too few of
        /// its frames were simulated (Advance(key, true)). The name is kept for its readers.</summary>
        public bool LastMonthSinceLoad { get; private set; }
        public int Revision { get; private set; }

        private readonly List<Entry> m_Entries = new List<Entry>(4);
        private readonly Dictionary<EntityRef, int> m_Index = new Dictionary<EntityRef, int>(4);

        /// <summary>Entries (one per ski area seen this load, plus Not linked).</summary>
        public int Count => m_Entries.Count;

        /// <summary>Entry <paramref name="i"/> in first-use order (0 ≤ i &lt; Count).</summary>
        public Entry At(int i)
        {
            return m_Entries[i];
        }

        public bool TryGet(EntityRef sign, out Entry entry)
        {
            if (m_Index.TryGetValue(sign, out int i))
            {
                entry = m_Entries[i];
                return true;
            }
            entry = default;
            return false;
        }

        /// <summary>Empty, NoMonth, flags false, Revision++.</summary>
        public void Reset()
        {
            m_Entries.Clear();
            m_Index.Clear();
            MonthKey = NoMonth;
            ThisMonthSinceLoad = false;
            LastMonthKnown = false;
            LastMonthSinceLoad = false;
            Revision++;
        }

        /// <summary>ch4 §4.6: start, roll or jump the month (header CONTRACT); = Advance(monthKey, false).</summary>
        public AdvanceResult Advance(int monthKey)
        {
            return Advance(monthKey, false);
        }

        /// <summary>Round M1 (D5): as Advance(monthKey), except that a roll also marks the closed month partial when
        /// <paramref name="closedMonthShort"/> (too few of its frames were simulated, SkiMoneyRules.IsShortMonth):
        /// LastMonthSinceLoad = ThisMonthSinceLoad || closedMonthShort. A start, a jump or the same key ignore it.</summary>
        public AdvanceResult Advance(int monthKey, bool closedMonthShort)
        {
            if (MonthKey == NoMonth)
            {
                MonthKey = monthKey;
                ThisMonthSinceLoad = true;
                Revision++;
                return AdvanceResult.Started;
            }
            if (monthKey == MonthKey)
                return AdvanceResult.None;

            // MonthKey != NoMonth here, so MonthKey + 1 cannot overflow below; a key at int.MaxValue
            // simply never rolls.
            bool rolled = MonthKey != int.MaxValue && monthKey == MonthKey + 1;
            for (int i = 0; i < m_Entries.Count; i++)
            {
                Entry e = m_Entries[i];
                e.Last = rolled ? e.This : default;
                e.This = default;
                m_Entries[i] = e;
            }
            if (rolled)
            {
                LastMonthKnown = true;
                LastMonthSinceLoad = ThisMonthSinceLoad || closedMonthShort;   // Round M1 (D5): "partial"
                ThisMonthSinceLoad = false;
            }
            else
            {
                LastMonthKnown = false;
                LastMonthSinceLoad = false;
                // E1 fixer (e1-spec §8.9 F8): a jump lands partway through a month, so counting restarts
                // there exactly as after a load; the month it starts is never called a full month.
                ThisMonthSinceLoad = true;
            }
            MonthKey = monthKey;
            Revision++;
            return rolled ? AdvanceResult.Rolled : AdvanceResult.Jumped;
        }

        /// <summary>tourist: TouristGuests += people, TouristParties++ when <paramref name="newGroup"/>,
        /// TouristOnLeisure += people when <paramref name="onLeisure"/>; else LocalGuests += people (both flags
        /// ignored). people ≤ 0 changes nothing (Round S1 D23, header).</summary>
        public void AddArrival(EntityRef sign, bool tourist, int people, bool newGroup = true, bool onLeisure = false)
        {
            if (people <= 0)
                return;
            int i = IndexOf(sign);
            Entry e = m_Entries[i];
            if (tourist)
            {
                e.This.TouristGuests += people;
                if (newGroup)
                    e.This.TouristParties++;
                if (onLeisure)
                    e.This.TouristOnLeisure += people;
            }
            else
            {
                e.This.LocalGuests += people;
            }
            m_Entries[i] = e;
            Revision++;
        }

        /// <summary>Adds the booked tickets of one batch; each input below 0 reads 0; all 0 changes nothing.</summary>
        public void AddTickets(EntityRef sign, int paid, int unpaid, int free, long income)
        {
            if (paid < 0)
                paid = 0;
            if (unpaid < 0)
                unpaid = 0;
            if (free < 0)
                free = 0;
            if (income < 0)
                income = 0;
            if (paid == 0 && unpaid == 0 && free == 0 && income == 0)
                return;
            int i = IndexOf(sign);
            Entry e = m_Entries[i];
            e.This.TicketsPaid += paid;
            e.This.TicketsUnpaid += unpaid;
            e.This.TicketsFree += free;
            e.This.TicketIncome += income;
            m_Entries[i] = e;
            Revision++;
        }

        /// <summary>Catalog Phase B (D15, seam S8): adds one batch of lodge spends (guests who spent, ₵ moved) under the
        /// AddTickets rules — each input below 0 reads 0; all 0 changes nothing (no entry is created); else the entry is
        /// created on first use and Revision moves.</summary>
        public void AddLodgeSpend(EntityRef sign, int spends, long income)
        {
            if (spends < 0)
                spends = 0;
            if (income < 0)
                income = 0;
            if (spends == 0 && income == 0)
                return;
            int i = IndexOf(sign);
            Entry e = m_Entries[i];
            e.This.LodgeSpends += spends;
            e.This.LodgeSpendIncome += income;
            m_Entries[i] = e;
            Revision++;
        }

        /// <summary>One batch of passes sold at this ski area's gate (count, ₵ paid) and of
        /// guests who came in on a pass (<paramref name="holderGuests"/>), under the AddTickets rules — each input below 0
        /// reads 0; all 0 changes nothing (no entry is created); else the entry is created on first use and Revision moves.
        /// <paramref name="preSeason"/> also counts the batch in the pre-season subset.</summary>
        public void AddPasses(EntityRef sign, int sold, long income, int holderGuests = 0, int seasonKey = NoSeason, bool preSeason = false)
        {
            if (sold < 0)
                sold = 0;
            if (income < 0)
                income = 0;
            if (holderGuests < 0)
                holderGuests = 0;
            if (sold == 0 && income == 0 && holderGuests == 0)
                return;
            int i = IndexOf(sign);
            Entry e = m_Entries[i];
            e.This.PassesSold += sold;
            e.This.PassIncome += income;
            e.This.PassHolderGuests += holderGuests;
            if (preSeason)
            {
                e.This.PassesPre += sold;      // a subset of PassesSold / PassIncome, never added on top of them
                e.This.PassIncomePre += income;
            }
            if (seasonKey != NoSeason && (sold > 0 || income > 0))
            {
                if (e.PassSeasonKey != seasonKey)
                {
                    e.PassSeasonKey = seasonKey;   // a new ski season: the totals start over
                    e.PassSeasonSold = 0;
                    e.PassSeasonIncome = 0;
                    e.PassSeasonPre = 0;
                    e.PassSeasonPreIncome = 0;
                }
                e.PassSeasonSold += sold;
                e.PassSeasonIncome += income;
                if (preSeason)
                {
                    e.PassSeasonPre += sold;
                    e.PassSeasonPreIncome += income;
                }
            }
            m_Entries[i] = e;
            Revision++;
        }

        /// <summary>One batch of lessons (people who took one, ₵ paid for them) under the
        /// AddTickets rules — each input below 0 reads 0; all 0 changes nothing (no entry is created); else the entry is created on
        /// first use and Revision moves.</summary>
        public void AddLessons(EntityRef sign, int lessons, long income)
        {
            if (lessons < 0)
                lessons = 0;
            if (income < 0)
                income = 0;
            if (lessons == 0 && income == 0)
                return;
            int i = IndexOf(sign);
            Entry e = m_Entries[i];
            e.This.Lessons += lessons;
            e.This.LessonIncome += income;
            m_Entries[i] = e;
            Revision++;
        }

        /// <summary>Adds what the lift charge accrued for this ski area (EntityRef.Null = Not linked);
        /// an amount that is not > 0 (NaN included) changes nothing.</summary>
        public void AddLiftCharge(EntityRef sign, double amount)
        {
            if (!(amount > 0.0) || double.IsInfinity(amount))
                return;
            int i = IndexOf(sign);
            Entry e = m_Entries[i];
            e.This.LiftCharged += amount;
            m_Entries[i] = e;
            Revision++;
        }

        /// <summary>Drops entries whose sign is not live; keeps EntityRef.Null; returns how many.
        /// Order of the kept entries is unchanged.</summary>
        public int Prune(HashSet<EntityRef> liveSigns)
        {
            int kept = 0;
            for (int i = 0; i < m_Entries.Count; i++)
            {
                Entry e = m_Entries[i];
                if (e.Sign.IsNull || (liveSigns != null && liveSigns.Contains(e.Sign)))
                    m_Entries[kept++] = e;
            }
            int removed = m_Entries.Count - kept;
            if (removed == 0)
                return 0;
            m_Entries.RemoveRange(kept, removed);
            m_Index.Clear();
            for (int i = 0; i < m_Entries.Count; i++)
                m_Index[m_Entries[i].Sign] = i;
            Revision++;
            return removed;
        }

        /// <summary>Sum over all entries (logs): last month when <paramref name="last"/>, else this month.</summary>
        public Month Total(bool last)
        {
            Month t = default;
            for (int i = 0; i < m_Entries.Count; i++)
            {
                Month m = last ? m_Entries[i].Last : m_Entries[i].This;
                t.TouristGuests += m.TouristGuests;
                t.TouristParties += m.TouristParties;
                t.TouristOnLeisure += m.TouristOnLeisure;
                t.LocalGuests += m.LocalGuests;
                t.TicketsPaid += m.TicketsPaid;
                t.TicketsUnpaid += m.TicketsUnpaid;
                t.TicketsFree += m.TicketsFree;
                t.TicketIncome += m.TicketIncome;
                t.LiftCharged += m.LiftCharged;
                t.LodgeSpends += m.LodgeSpends;              // Catalog Phase B (D15)
                t.LodgeSpendIncome += m.LodgeSpendIncome;
                t.PassesSold += m.PassesSold;                // Season passes v1 (DECISIONS 240)
                t.PassIncome += m.PassIncome;
                t.PassHolderGuests += m.PassHolderGuests;
                t.PassesPre += m.PassesPre;                  // Econ-2 round 2 (DECISIONS 241)
                t.PassIncomePre += m.PassIncomePre;
                t.Lessons += m.Lessons;
                t.LessonIncome += m.LessonIncome;
            }
            return t;
        }

        /// <summary>The entry of <paramref name="sign"/>, created (zeroed) on first use.</summary>
        private int IndexOf(EntityRef sign)
        {
            if (m_Index.TryGetValue(sign, out int i))
                return i;
            i = m_Entries.Count;
            m_Entries.Add(new Entry { Sign = sign });
            m_Index[sign] = i;
            return i;
        }
    }
}
