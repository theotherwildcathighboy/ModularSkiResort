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
            public int Guests => TouristGuests + LocalGuests;
        }

        public struct Entry { public EntityRef Sign; public Month This, Last; }

        public enum AdvanceResult : byte { None = 0, Started = 1, Rolled = 2, Jumped = 3 }

        public const int NoMonth = int.MinValue;
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
