// SkiResortLedger - the pure monthly guest and money ledger: one Entry per ski area keyed by its sign (EntityRef.Null = Not linked,
// lift charges only). In memory only, reset on every load; the month in progress is restored from SkiResortMonthInProgress.
// SkiGuestCounterSystem owns the one instance (Advance / AddArrival / Prune). The ledger never subtracts: inputs below 0 are ignored.
// Closed months go to the per-sign history buffer (SkiResortHistory.cs), not here.
// Pure file: System, System.Collections.Generic plus EntityRef only (tests/ResortBatch links it).
// Engine notes: EngineNotes/SkiResortLedger.md

using System;
using System.Collections.Generic;

namespace SkiResortMod.Systems
{
    public sealed class SkiResortLedger
    {
        public struct Month
        {
            public int TouristGuests, TouristParties, LocalGuests;
            public int TouristOnLeisure;    // tourists counted through the leisure scan (subset of TouristGuests)
            public int TicketsPaid, TicketsUnpaid, TicketsFree;
            public long TicketIncome;
            public double LiftCharged;
            public int LodgeSpends;          // paying guests who also spent at the lodge they arrived at
            public long LodgeSpendIncome;    // ₵ moved from their households to the city for it
            // Passes sold at this ski area's gate, what they paid, and the guests who came in on a pass (a holder's arrival: not
            // Paid, not Unpaid, not Free). All 0 while passes are off.
            public int PassesSold;
            public long PassIncome;
            public int PassHolderGuests;
            // PassesPre / PassIncomePre = the part of PassesSold / PassIncome sold in the pre-season sale;
            // Lessons / LessonIncome = lessons taken (one per person) and what they paid.
            public int PassesPre;
            public long PassIncomePre;
            public int Lessons;
            public long LessonIncome;
            // The run upkeep debited from the city for this ski area's runs (whole credits; EntityRef.Null = unbound runs and runs
            // the catalog does not list yet). Booked by SkiRunUpkeepSystem.
            public long RunUpkeep;
            public int Guests => TouristGuests + LocalGuests;
        }

        // Hub: the pass sales of the ski season in PassSeasonKey (SkiMoneyRules.SeasonKeyOf), kept across month rolls (Advance
        // never touches them); a reader asks PassSeasonSold / PassSeasonIncome for the season it wants and gets 0 for another.
        // PassSeasonPre / PassSeasonPreIncome = the pre-season sale's part of those totals (a subset, never added twice).
        public struct Entry
        {
            public EntityRef Sign; public Month This, Last; public int PassSeasonKey, PassSeasonSold; public long PassSeasonIncome;
            public int PassSeasonPre; public long PassSeasonPreIncome;
        }

        public enum AdvanceResult : byte { None = 0, Started = 1, Rolled = 2, Jumped = 3 }

        public const int NoMonth = int.MinValue;
        public const int NoSeason = int.MinValue;   // AddPasses without a season: the season totals are left alone

        /// <summary>Passes sold at this ski area in <paramref name="seasonKey"/> (0 for any other season)</summary>
        public static int PassSeasonSoldOf(in Entry e, int seasonKey) => e.PassSeasonKey == seasonKey ? e.PassSeasonSold : 0;

        /// <summary>What those passes paid (0 for any other season)</summary>
        public static long PassSeasonIncomeOf(in Entry e, int seasonKey) => e.PassSeasonKey == seasonKey ? e.PassSeasonIncome : 0L;

        /// <summary>The passes of <paramref name="seasonKey"/> that were sold in the pre-season sale (a subset of PassSeasonSoldOf)</summary>
        public static int PassSeasonPreOf(in Entry e, int seasonKey) => e.PassSeasonKey == seasonKey ? e.PassSeasonPre : 0;

        /// <summary>What those pre-season passes paid (a subset of PassSeasonIncomeOf)</summary>
        public static long PassSeasonPreIncomeOf(in Entry e, int seasonKey) => e.PassSeasonKey == seasonKey ? e.PassSeasonPreIncome : 0L;
        public int MonthKey { get; private set; } = NoMonth;
        public bool ThisMonthSinceLoad { get; private set; }
        public bool LastMonthKnown { get; private set; }
        /// <summary>Last month partial: counting started in it (a load or a jump), or too few of its frames were simulated
        /// (Advance(key, true)). The name is kept for its readers.</summary>
        public bool LastMonthSinceLoad { get; private set; }
        public int Revision { get; private set; }

        private readonly List<Entry> m_Entries = new List<Entry>(4);
        private readonly Dictionary<EntityRef, int> m_Index = new Dictionary<EntityRef, int>(4);

        public int Count => m_Entries.Count;

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

        /// <summary>Start, roll or jump the month (see the engine notes); = Advance(monthKey, false)</summary>
        public AdvanceResult Advance(int monthKey)
        {
            return Advance(monthKey, false);
        }

        /// <summary>As Advance(monthKey), except that a roll also marks the closed month partial when
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

            // MonthKey != NoMonth here, so MonthKey + 1 cannot overflow below; a key at int.MaxValue simply never rolls
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
                LastMonthSinceLoad = ThisMonthSinceLoad || closedMonthShort;
                ThisMonthSinceLoad = false;
            }
            else
            {
                LastMonthKnown = false;
                LastMonthSinceLoad = false;
                // A jump lands partway through a month, so counting restarts there exactly as after a load; the month it starts is
                // never called a full month.
                ThisMonthSinceLoad = true;
            }
            MonthKey = monthKey;
            Revision++;
            return rolled ? AdvanceResult.Rolled : AdvanceResult.Jumped;
        }

        /// <summary>tourist: TouristGuests += people, TouristParties++ when <paramref name="newGroup"/>, TouristOnLeisure +=
        /// people when <paramref name="onLeisure"/>; else LocalGuests += people (both flags ignored). people &lt;= 0 changes nothing.</summary>
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

        /// <summary>One batch of lodge spends (guests who spent, credits moved) under the AddTickets rules: each input below 0
        /// reads 0; all 0 changes nothing (no entry is created)</summary>
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

        /// <summary>One batch of passes sold at this ski area's gate (count, credits paid) and of guests who came in on a pass
        /// (<paramref name="holderGuests"/>), under the AddTickets rules. <paramref name="preSeason"/> also counts the batch in
        /// the pre-season subset.</summary>
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
                e.This.PassesPre += sold;
                e.This.PassIncomePre += income;
            }
            if (seasonKey != NoSeason && (sold > 0 || income > 0))
            {
                if (e.PassSeasonKey != seasonKey)
                {
                    e.PassSeasonKey = seasonKey;
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

        /// <summary>One batch of lessons (people who took one, credits paid) under the AddTickets rules</summary>
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

        /// <summary>Adds what the lift charge accrued for this ski area (EntityRef.Null = Not linked); an amount that is not > 0
        /// (NaN included) changes nothing.</summary>
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

        /// <summary>Adds the run upkeep debited for this ski area's runs (EntityRef.Null = Not linked); an amount &lt;= 0 changes
        /// nothing (no entry is created)</summary>
        public void AddRunUpkeep(EntityRef sign, long amount)
        {
            if (amount <= 0)
                return;
            int i = IndexOf(sign);
            Entry e = m_Entries[i];
            e.This.RunUpkeep += amount;
            m_Entries[i] = e;
            Revision++;
        }

        /// <summary>Accepts a saved month in progress for the ledger's month. False, and nothing changes, unless the ledger has
        /// started (MonthKey != NoMonth) and <paramref name="monthKey"/> is its month. True: ThisMonthSinceLoad =
        /// <paramref name="partial"/> (the saved month's own state), Revision++. The caller then adds each area's saved numbers
        /// with SeedThis.</summary>
        public bool SeedMonth(int monthKey, bool partial)
        {
            if (MonthKey == NoMonth || monthKey != MonthKey)
                return false;
            ThisMonthSinceLoad = partial;
            Revision++;
            return true;
        }

        /// <summary>Adds a saved month in progress into this ski area's This (whatever was booked since the load stays: the
        /// first scans after a load are baselines, so nothing is counted twice), and its pass season totals: added when the entry
        /// holds the same season, taken when it holds none, ignored when it already holds another season. Inputs below 0 read 0.
        /// Call only after SeedMonth returned true. An all-zero month changes nothing.</summary>
        public void SeedThis(EntityRef sign, in Month saved, int passSeasonKey, int passSeasonSold, long passSeasonIncome,
                             int passSeasonPre, long passSeasonPreIncome)
        {
            bool season = passSeasonSold > 0 || passSeasonIncome > 0;
            if (IsZero(saved) && !season)
                return;
            int i = IndexOf(sign);
            Entry e = m_Entries[i];
            Month m = e.This;
            m.TouristGuests += Math.Max(0, saved.TouristGuests);
            m.TouristParties += Math.Max(0, saved.TouristParties);
            m.LocalGuests += Math.Max(0, saved.LocalGuests);
            m.TouristOnLeisure += Math.Max(0, saved.TouristOnLeisure);
            m.TicketsPaid += Math.Max(0, saved.TicketsPaid);
            m.TicketsUnpaid += Math.Max(0, saved.TicketsUnpaid);
            m.TicketsFree += Math.Max(0, saved.TicketsFree);
            m.TicketIncome += Math.Max(0L, saved.TicketIncome);
            if (saved.LiftCharged > 0.0 && !double.IsInfinity(saved.LiftCharged))
                m.LiftCharged += saved.LiftCharged;
            m.LodgeSpends += Math.Max(0, saved.LodgeSpends);
            m.LodgeSpendIncome += Math.Max(0L, saved.LodgeSpendIncome);
            m.PassesSold += Math.Max(0, saved.PassesSold);
            m.PassIncome += Math.Max(0L, saved.PassIncome);
            m.PassHolderGuests += Math.Max(0, saved.PassHolderGuests);
            m.PassesPre += Math.Max(0, saved.PassesPre);
            m.PassIncomePre += Math.Max(0L, saved.PassIncomePre);
            m.Lessons += Math.Max(0, saved.Lessons);
            m.LessonIncome += Math.Max(0L, saved.LessonIncome);
            m.RunUpkeep += Math.Max(0L, saved.RunUpkeep);
            e.This = m;
            if (season && passSeasonKey != NoSeason)
            {
                bool empty = e.PassSeasonSold == 0 && e.PassSeasonIncome == 0;
                if (empty && e.PassSeasonKey != passSeasonKey)
                {
                    e.PassSeasonKey = passSeasonKey;
                    e.PassSeasonSold = e.PassSeasonPre = 0;
                    e.PassSeasonIncome = e.PassSeasonPreIncome = 0;
                }
                if (e.PassSeasonKey == passSeasonKey)
                {
                    e.PassSeasonSold += Math.Max(0, passSeasonSold);
                    e.PassSeasonIncome += Math.Max(0L, passSeasonIncome);
                    e.PassSeasonPre += Math.Max(0, passSeasonPre);
                    e.PassSeasonPreIncome += Math.Max(0L, passSeasonPreIncome);
                }
            }
            m_Entries[i] = e;
            Revision++;
        }

        private static bool IsZero(in Month m)
        {
            return m.TouristGuests <= 0 && m.TouristParties <= 0 && m.LocalGuests <= 0 && m.TouristOnLeisure <= 0 &&
                   m.TicketsPaid <= 0 && m.TicketsUnpaid <= 0 && m.TicketsFree <= 0 && m.TicketIncome <= 0 &&
                   !(m.LiftCharged > 0.0) && m.LodgeSpends <= 0 && m.LodgeSpendIncome <= 0 && m.PassesSold <= 0 &&
                   m.PassIncome <= 0 && m.PassHolderGuests <= 0 && m.PassesPre <= 0 && m.PassIncomePre <= 0 &&
                   m.Lessons <= 0 && m.LessonIncome <= 0 && m.RunUpkeep <= 0;
        }

        /// <summary>Drops entries whose sign is not live; keeps EntityRef.Null; returns how many. Order of the kept entries is unchanged.</summary>
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
                t.LodgeSpends += m.LodgeSpends;
                t.LodgeSpendIncome += m.LodgeSpendIncome;
                t.PassesSold += m.PassesSold;
                t.PassIncome += m.PassIncome;
                t.PassHolderGuests += m.PassHolderGuests;
                t.PassesPre += m.PassesPre;
                t.PassIncomePre += m.PassIncomePre;
                t.Lessons += m.Lessons;
                t.LessonIncome += m.LessonIncome;
                t.RunUpkeep += m.RunUpkeep;
            }
            return t;
        }

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
