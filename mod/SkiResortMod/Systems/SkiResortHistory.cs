// Pure part of the hub's history store, linked by tests/ResortBatch (HistoryTests.cs). It holds SkiResortHistoryMonth,
// SkiResortMonthInProgress, the ring / season / bucket ops of SkiResortHistory and the seams IHistoryWriter / IHistoryReader /
// IHistoryStore. The ECS halves are the other `partial` in SkiResortHistorySystem.cs. This file writes the bytes, so the
// round-trip test checks what the save gets.
// Save layouts are frozen once shipped: no payload length is written. Never reorder, retype or rename these types,
// move their namespace or turn the buffer into a component.
// A new field is appended under a version bump (EngineNotes/SaveLayouts.md). The element is v2 since T142 (m_SnowDepthCm, B7b).
// A v1 element reads it as unknown.
// Money in whole ₵ as int32, clamped. Unknown: skiDays / buildingsCost / wagesCost / attraction / snowDepthCm -1, snowPct 255.
// Net is never stored.
// Pure file: System, System.Collections.Generic, Unity.Mathematics (SkiMoneyRules), SkiResortLedger, HubRules and EntityRef only.
// Engine notes: EngineNotes/SkiResortHistory.md

using System;
using System.Collections.Generic;

namespace SkiResortMod.Systems
{
    /// <summary>The bytes a codec writes (Colossal IWriter in the mod, a byte list in the tests). Little-endian.</summary>
    public interface IHistoryWriter
    {
        void Write(byte value);
        void Write(int value);
        void Write(long value);
        void Write(double value);
    }

    /// <summary>The bytes a codec reads back, in the order IHistoryWriter wrote them.</summary>
    public interface IHistoryReader
    {
        void Read(out byte value);
        void Read(out int value);
        void Read(out long value);
        void Read(out double value);
    }

    /// <summary>An ordered element store (DynamicBuffer in the mod, a List in the tests).</summary>
    public interface IHistoryStore
    {
        int Count { get; }
        SkiResortHistoryMonth this[int i] { get; set; }
        void Add(SkiResortHistoryMonth m);
        void RemoveRange(int index, int count);
    }

    /// <summary>SkiResortHistoryMonth.m_Flags bits (Engine notes: Flags).</summary>
    public static class HistoryFlags
    {
        public const byte Partial = 1;
        public const byte Short = 2;
        public const byte CostsEstimated = 4; // reserved in v1
        public const byte SnapshotMissing = 8;
        public const byte Gap = 16;
        public const byte SkiDaysPartial = 32;
        public const byte First = 64;
    }

    public partial struct SkiResortHistoryMonth
    {
        /// <summary>Layout version this build writes: v2 = v1 + m_SnowDepthCm (T142 B7b). v1 is frozen (engine notes).</summary>
        public const byte CurrentVersion = 2;
        /// <summary>Bytes one v1 element takes in the save (the round-trip test pins it).</summary>
        public const int V1Bytes = 59;
        /// <summary>Bytes one v2 element takes: v1 + int32 m_SnowDepthCm.</summary>
        public const int V2Bytes = V1Bytes + 4;
        public const int Unknown = -1;
        public const byte SnowUnknown = 255;

        /// <summary>Layout version this value was created with or loaded from. Not written as such: WriteTo writes CurrentVersion.</summary>
        public byte m_Version;
        public byte m_Flags;
        public int m_MonthKey; // SkiMoneyRules.MonthKey of the closed month
        public int m_Tourists; // ledger TouristGuests
        public int m_Residents; // ledger LocalGuests
        public int m_SkiDays; // SkiDaySystem arrivals of the month. -1 unknown (ski days off / not measured)
        public int m_TicketsPaid; // ledger TicketsPaid
        public int m_TicketIncome;
        public int m_LodgeIncome; // ledger LodgeSpendIncome
        public int m_PassIncome;
        public int m_LessonIncome;
        public int m_LiftCost; // the ledger's LiftCharged + BusCharged rounded (a closed month keeps no bus column: History net stays true)
        public int m_RunCost; // the ledger's RunUpkeep (booked per area)
        public int m_BuildingsCost; // per-month rate at the roll. -1 unknown
        public int m_WagesCost; // per-month rate at the roll. -1 unknown
        public int m_Attraction; // point value at the roll. -1 unknown
        public byte m_SnowPct; // 0..100 at the roll. 255 unknown
        public int m_SnowDepthCm; // v2: area-weighted mean depth on the runs at the roll, whole cm. -1 unknown (and every v1 element)

        public bool IsGap => (m_Flags & HistoryFlags.Gap) != 0;

        /// <summary>The element's eight Net terms, as stored (buildings / wages -1 unknown, the coach bill is inside LiftCost). HubRules.NetMonth over them is the
        /// month's Net, the number historyRules.ts finish() derives (income - lift - run - max(0, buildings) - max(0, wages)).</summary>
        public HubMonthMoney Money => new HubMonthMoney
        {
            TicketIncome = m_TicketIncome, LodgeIncome = m_LodgeIncome, LessonIncome = m_LessonIncome, PassIncome = m_PassIncome,
            LiftCost = m_LiftCost, RunCost = m_RunCost, BuildingsCost = m_BuildingsCost, WagesCost = m_WagesCost,
        };

        /// <summary>The GAP marker at <paramref name="monthKey"/> (Engine notes: Flags): every value unknown or 0.</summary>
        public static SkiResortHistoryMonth Gap(int monthKey)
        {
            return new SkiResortHistoryMonth
            {
                m_Version = CurrentVersion, m_Flags = HistoryFlags.Gap, m_MonthKey = monthKey, m_SkiDays = Unknown,
                m_BuildingsCost = Unknown, m_WagesCost = Unknown, m_Attraction = Unknown, m_SnowPct = SnowUnknown, m_SnowDepthCm = Unknown,
            };
        }

        /// <summary>The booked part of a closed month from the ledger (the snapshot fields read unknown, flags as given).</summary>
        public static SkiResortHistoryMonth FromLedger(in SkiResortLedger.Month m, int monthKey, byte flags)
        {
            return new SkiResortHistoryMonth
            {
                m_Version = CurrentVersion, m_Flags = flags, m_MonthKey = monthKey,
                m_Tourists = Math.Max(0, m.TouristGuests), m_Residents = Math.Max(0, m.LocalGuests), m_SkiDays = Unknown,
                m_TicketsPaid = Math.Max(0, m.TicketsPaid),
                m_TicketIncome = SkiResortHistory.ClampInt(m.TicketIncome),
                m_LodgeIncome = SkiResortHistory.ClampInt(m.LodgeSpendIncome),
                m_PassIncome = SkiResortHistory.ClampInt(m.PassIncome),
                m_LessonIncome = SkiResortHistory.ClampInt(m.LessonIncome),
                // The coach bill folds into the lift column: same accrual, no history layout or UI series for one small term.
                m_LiftCost = SkiResortHistory.RoundMoney(m.LiftCharged + m.BusCharged),
                m_RunCost = SkiResortHistory.ClampInt(m.RunUpkeep),
                m_BuildingsCost = Unknown, m_WagesCost = Unknown, m_Attraction = Unknown, m_SnowPct = SnowUnknown, m_SnowDepthCm = Unknown,
            };
        }

        /// <summary>Snow cover % for the element: rounded, clamped 0..100. A value not finite or below 0 gives 255 (unknown).</summary>
        public static byte SnowByte(float pct)
        {
            if (float.IsNaN(pct) || float.IsInfinity(pct) || pct < 0f)
                return SnowUnknown;
            return (byte)Math.Min(100, (int)Math.Round(pct, MidpointRounding.AwayFromZero));
        }

        /// <summary>Snow depth for the element (ResortLive.SnowDepthCm): whole cm, rounded. A value not finite or below 0 gives -1 (unknown).
        /// The method clamps to 100,000 cm (1 km, far above any snow), so a stray value never wraps.</summary>
        public static int DepthCm(float cm)
        {
            if (float.IsNaN(cm) || float.IsInfinity(cm) || cm < 0f)
                return Unknown;
            return (int)Math.Min(100000.0, Math.Round(cm, MidpointRounding.AwayFromZero));
        }

        /// <summary>v2 layout, byte for byte (Engine notes: Save layout): the v1 fields, then m_SnowDepthCm. Version first, always
        /// CurrentVersion.</summary>
        public void WriteTo<TWriter>(ref TWriter w) where TWriter : IHistoryWriter
        {
            byte version = CurrentVersion;
            w.Write(version);
            w.Write(m_Flags);
            w.Write(m_MonthKey);
            w.Write(m_Tourists);
            w.Write(m_Residents);
            w.Write(m_SkiDays);
            w.Write(m_TicketsPaid);
            w.Write(m_TicketIncome);
            w.Write(m_LodgeIncome);
            w.Write(m_PassIncome);
            w.Write(m_LessonIncome);
            w.Write(m_LiftCost);
            w.Write(m_RunCost);
            w.Write(m_BuildingsCost);
            w.Write(m_WagesCost);
            w.Write(m_Attraction);
            w.Write(m_SnowPct);
            w.Write(m_SnowDepthCm);   // v2
        }

        /// <summary>v1 fields (frozen, see engine notes), then the v2 field when the element is v2 or newer. A v1 element reads
        /// m_SnowDepthCm as unknown.</summary>
        public void ReadFrom<TReader>(ref TReader r) where TReader : IHistoryReader
        {
            r.Read(out m_Version);
            r.Read(out m_Flags);
            r.Read(out m_MonthKey);
            r.Read(out m_Tourists);
            r.Read(out m_Residents);
            r.Read(out m_SkiDays);
            r.Read(out m_TicketsPaid);
            r.Read(out m_TicketIncome);
            r.Read(out m_LodgeIncome);
            r.Read(out m_PassIncome);
            r.Read(out m_LessonIncome);
            r.Read(out m_LiftCost);
            r.Read(out m_RunCost);
            r.Read(out m_BuildingsCost);
            r.Read(out m_WagesCost);
            r.Read(out m_Attraction);
            r.Read(out m_SnowPct);
            if (m_Version >= 2)
                r.Read(out m_SnowDepthCm);
            else
                m_SnowDepthCm = Unknown;
        }
    }

    // LAYOUT v1 (142 bytes): byte version | byte flags | int32 monthKey | int32 framesSimulated | the whole ledger Month in its
    // declaration order - int32 TouristGuests, TouristParties, LocalGuests, TouristOnLeisure, TicketsPaid, TicketsUnpaid,
    // TicketsFree | int64 TicketIncome | double LiftCharged | int32 LodgeSpends | int64 LodgeSpendIncome | int32 PassesSold |
    // int64 PassIncome | int32 PassHolderGuests | int32 PassesPre | int64 PassIncomePre | int32 Lessons | int64 LessonIncome |
    // int64 RunUpkeep - then the entry's pass season: int32 PassSeasonKey | int32 PassSeasonSold | int64 PassSeasonIncome |
    // int32 PassSeasonPre | int64 PassSeasonPreIncome.
    // = 2 + 4 + 4 + 7x4 + 8 + 8 + 4 + 8 + 4 + 8 + 4 + 4 + 8 + 4 + 8 + 8 + 4 + 4 + 8 + 4 + 8 = 142 bytes (V1Bytes).
    // LAYOUT v2 (150 bytes): v1, then double m_Month.BusCharged (the ski bus depot's coach bill).
    public partial struct SkiResortMonthInProgress
    {
        public const byte CurrentVersion = 2;
        /// <summary>Bytes of a v1 value in the save (pinned by HistoryTests).</summary>
        public const int V1Bytes = 2 + 4 + 4 + 7 * 4 + 8 + 8 + 4 + 8 + 4 + 8 + 4 + 4 + 8 + 4 + 8 + 8 + 4 + 4 + 8 + 4 + 8;
        /// <summary>Bytes of a v2 value: v1 + double m_Month.BusCharged.</summary>
        public const int V2Bytes = V1Bytes + 8;

        public const byte FlagPartial = 1; // the ledger's ThisMonthSinceLoad at the mirror (counting started partway)
        public const byte FlagByJump = 2; // together with FlagPartial: counting started at a calendar jump (the counter's label), not at a load

        public byte m_Version;
        public byte m_Flags;
        public int m_MonthKey; // SkiResortLedger.NoMonth = nothing mirrored yet
        public int m_FramesSimulated; // sim frames of this month simulated before the mirror (the 95 % short rule)
        public SkiResortLedger.Month m_Month;
        public int m_PassSeasonKey, m_PassSeasonSold, m_PassSeasonPre;
        public long m_PassSeasonIncome, m_PassSeasonPreIncome;

        /// <summary>The mirror of one ledger entry.</summary>
        public static SkiResortMonthInProgress From(in SkiResortLedger.Entry e, int monthKey, bool partial, bool byJump, uint framesSimulated)
        {
            return new SkiResortMonthInProgress
            {
                m_Version = CurrentVersion,
                m_Flags = (byte)((partial ? FlagPartial : 0) | (partial && byJump ? FlagByJump : 0)),
                m_MonthKey = monthKey,
                m_FramesSimulated = framesSimulated > int.MaxValue ? int.MaxValue : (int)framesSimulated,
                m_Month = e.This,
                m_PassSeasonKey = e.PassSeasonKey, m_PassSeasonSold = e.PassSeasonSold, m_PassSeasonIncome = e.PassSeasonIncome,
                m_PassSeasonPre = e.PassSeasonPre, m_PassSeasonPreIncome = e.PassSeasonPreIncome,
            };
        }

        public void WriteTo<TWriter>(ref TWriter w) where TWriter : IHistoryWriter
        {
            byte version = CurrentVersion;
            w.Write(version);
            w.Write(m_Flags);
            w.Write(m_MonthKey);
            w.Write(m_FramesSimulated);
            w.Write(m_Month.TouristGuests);
            w.Write(m_Month.TouristParties);
            w.Write(m_Month.LocalGuests);
            w.Write(m_Month.TouristOnLeisure);
            w.Write(m_Month.TicketsPaid);
            w.Write(m_Month.TicketsUnpaid);
            w.Write(m_Month.TicketsFree);
            w.Write(m_Month.TicketIncome);
            w.Write(m_Month.LiftCharged);
            w.Write(m_Month.LodgeSpends);
            w.Write(m_Month.LodgeSpendIncome);
            w.Write(m_Month.PassesSold);
            w.Write(m_Month.PassIncome);
            w.Write(m_Month.PassHolderGuests);
            w.Write(m_Month.PassesPre);
            w.Write(m_Month.PassIncomePre);
            w.Write(m_Month.Lessons);
            w.Write(m_Month.LessonIncome);
            w.Write(m_Month.RunUpkeep);
            w.Write(m_PassSeasonKey);
            w.Write(m_PassSeasonSold);
            w.Write(m_PassSeasonIncome);
            w.Write(m_PassSeasonPre);
            w.Write(m_PassSeasonPreIncome);
            w.Write(m_Month.BusCharged);   // v2
        }

        /// <summary>v1 fields (frozen, see engine notes), then the v2 field when the value is v2 or newer. A v1 value reads no coach bill.</summary>
        public void ReadFrom<TReader>(ref TReader r) where TReader : IHistoryReader
        {
            r.Read(out m_Version);
            r.Read(out m_Flags);
            r.Read(out m_MonthKey);
            r.Read(out m_FramesSimulated);
            r.Read(out m_Month.TouristGuests);
            r.Read(out m_Month.TouristParties);
            r.Read(out m_Month.LocalGuests);
            r.Read(out m_Month.TouristOnLeisure);
            r.Read(out m_Month.TicketsPaid);
            r.Read(out m_Month.TicketsUnpaid);
            r.Read(out m_Month.TicketsFree);
            r.Read(out m_Month.TicketIncome);
            r.Read(out m_Month.LiftCharged);
            r.Read(out m_Month.LodgeSpends);
            r.Read(out m_Month.LodgeSpendIncome);
            r.Read(out m_Month.PassesSold);
            r.Read(out m_Month.PassIncome);
            r.Read(out m_Month.PassHolderGuests);
            r.Read(out m_Month.PassesPre);
            r.Read(out m_Month.PassIncomePre);
            r.Read(out m_Month.Lessons);
            r.Read(out m_Month.LessonIncome);
            r.Read(out m_Month.RunUpkeep);
            r.Read(out m_PassSeasonKey);
            r.Read(out m_PassSeasonSold);
            r.Read(out m_PassSeasonIncome);
            r.Read(out m_PassSeasonPre);
            r.Read(out m_PassSeasonPreIncome);
            if (m_Version >= 2)
                r.Read(out m_Month.BusCharged);
            else
                m_Month.BusCharged = 0.0;
        }
    }

    public static class SkiResortHistory
    {
        /// <summary>Safety cap only (no range cap): 100 in-game years. The oldest elements go first.</summary>
        public const int CapMonths = 1200;

        public enum AppendResult : byte { Appended = 0, AppendedAfterGap = 1, Replaced = 2, Rejected = 3 }

        public static int ClampInt(long v) => v > int.MaxValue ? int.MaxValue : v < int.MinValue ? int.MinValue : (int)v;

        /// <summary>A ₵ amount (double) rounded half away from zero and clamped to int32. Not finite gives 0.</summary>
        public static int RoundMoney(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v))
                return 0;
            double r = Math.Round(v, MidpointRounding.AwayFromZero);
            return r >= int.MaxValue ? int.MaxValue : r <= int.MinValue ? int.MinValue : (int)r;
        }

        /// <summary>The key of the last non-Gap element, or SkiResortLedger.NoMonth.</summary>
        public static int LastKey<TStore>(ref TStore store) where TStore : IHistoryStore
        {
            return store.Count > 0 ? store[store.Count - 1].m_MonthKey : SkiResortLedger.NoMonth;
        }

        /// <summary>Appends <paramref name="m"/> (keys strictly increasing). The same key as the last element replaces it (the later
        /// sample wins). A key more than one month after the last inserts one Gap marker at last + 1 first. An older key is Rejected (a
        /// backward calendar jump: history never rewrites its past). An empty store just appends (the First flag is the caller's).
        /// Then it drops the oldest elements above CapMonths. It drops a leading Gap marker left at the front too: that marker would
        /// describe months before the store's start.</summary>
        public static AppendResult Append<TStore>(ref TStore store, SkiResortHistoryMonth m) where TStore : IHistoryStore
        {
            AppendResult result = AppendResult.Appended;
            int n = store.Count;
            if (n > 0)
            {
                int last = store[n - 1].m_MonthKey;
                if (m.m_MonthKey == last)
                {
                    store[n - 1] = m;
                    return AppendResult.Replaced;
                }
                if (m.m_MonthKey < last)
                    return AppendResult.Rejected;
                if (last != int.MaxValue && m.m_MonthKey > last + 1 && !store[n - 1].IsGap)
                {
                    store.Add(SkiResortHistoryMonth.Gap(last + 1));
                    result = AppendResult.AppendedAfterGap;
                }
                else if (store[n - 1].IsGap)
                {
                    // the last element is already a marker (a jump recorded earlier): it covers up to this key
                    result = AppendResult.AppendedAfterGap;
                }
            }
            store.Add(m);
            Trim(ref store, CapMonths);
            return result;
        }

        /// <summary>Keeps at most <paramref name="cap"/> elements (the newest). Returns how many were dropped.</summary>
        public static int Trim<TStore>(ref TStore store, int cap) where TStore : IHistoryStore
        {
            int drop = store.Count - Math.Max(1, cap);
            if (drop <= 0)
                return 0;
            while (drop < store.Count - 1 && store[drop].IsGap)
                drop++; // never start the store with a marker
            store.RemoveRange(0, drop);
            return drop;
        }

        /// <summary>The ski season (July-June, SkiMoneyRules.SeasonKeyOf) of a month key.</summary>
        public static int SeasonOf(int monthKey)
            => SkiMoneyRules.SeasonKeyOf(SkiMoneyRules.YearOfKey(monthKey), SkiMoneyRules.MonthOfKey(monthKey));

        /// <summary>The month key of July of season <paramref name="seasonKey"/> (its first month).</summary>
        public static int SeasonFirstKey(int seasonKey) => seasonKey * 12 + (SkiMoneyRules.SeasonFirstMonth - 1);

        /// <summary>Flows summed, point values averaged over the months that know them.</summary>
        public struct Bucket
        {
            public int FirstKey, LastKey;
            public int Months; // months with data (Gap months and months not in the store are not counted)
            public byte Flags; // OR of the flags of the months counted
            public long Tourists, Residents, TicketsPaid, TicketIncome, LodgeIncome, PassIncome, LessonIncome, LiftCost, RunCost;
            public long SkiDays; public int SkiDaysMonths; // over months with skiDays known
            public long BuildingsCost; public int BuildingsMonths; // over months with the rate known
            public long WagesCost; public int WagesMonths;
            public float Attraction; // mean over months known. -1 none
            public float SnowPct; // mean over months known. -1 none
            public float SnowDepthCm; // mean over months known (v2 elements). -1 none
            public long Income => TicketIncome + LodgeIncome + PassIncome + LessonIncome;
            public long Guests => Tourists + Residents;
        }

        /// <summary>The bucket of months <paramref name="firstKey"/>..<paramref name="lastKey"/> (inclusive).</summary>
        public static Bucket Sum<TStore>(ref TStore store, int firstKey, int lastKey) where TStore : IHistoryStore
        {
            Bucket b = new Bucket { FirstKey = firstKey, LastKey = lastKey, Attraction = -1f, SnowPct = -1f, SnowDepthCm = -1f };
            long attrSum = 0, snowSum = 0, depthSum = 0;
            int attrN = 0, snowN = 0, depthN = 0;
            for (int i = 0; i < store.Count; i++)
            {
                SkiResortHistoryMonth m = store[i];
                if (m.IsGap || m.m_MonthKey < firstKey || m.m_MonthKey > lastKey)
                    continue;
                b.Months++;
                b.Flags |= m.m_Flags;
                b.Tourists += m.m_Tourists;
                b.Residents += m.m_Residents;
                b.TicketsPaid += m.m_TicketsPaid;
                b.TicketIncome += m.m_TicketIncome;
                b.LodgeIncome += m.m_LodgeIncome;
                b.PassIncome += m.m_PassIncome;
                b.LessonIncome += m.m_LessonIncome;
                b.LiftCost += m.m_LiftCost;
                b.RunCost += m.m_RunCost;
                if (m.m_SkiDays >= 0) { b.SkiDays += m.m_SkiDays; b.SkiDaysMonths++; }
                if (m.m_BuildingsCost >= 0) { b.BuildingsCost += m.m_BuildingsCost; b.BuildingsMonths++; }
                if (m.m_WagesCost >= 0) { b.WagesCost += m.m_WagesCost; b.WagesMonths++; }
                if (m.m_Attraction >= 0) { attrSum += m.m_Attraction; attrN++; }
                if (m.m_SnowPct != SkiResortHistoryMonth.SnowUnknown) { snowSum += m.m_SnowPct; snowN++; }
                if (m.m_SnowDepthCm >= 0) { depthSum += m.m_SnowDepthCm; depthN++; }
            }
            if (attrN > 0)
                b.Attraction = (float)attrSum / attrN;
            if (snowN > 0)
                b.SnowPct = (float)snowSum / snowN;
            if (depthN > 0)
                b.SnowDepthCm = (float)depthSum / depthN;
            return b;
        }

        /// <summary>The bucket of ski season <paramref name="seasonKey"/> (July of that year to June of the next).</summary>
        public static Bucket SumSeason<TStore>(ref TStore store, int seasonKey) where TStore : IHistoryStore
        {
            int first = SeasonFirstKey(seasonKey);
            return Sum(ref store, first, first + 11);
        }

        /// <summary>Splits one integer debit over <paramref name="count"/> slots by weight. The parts sum exactly to
        /// <paramref name="debit"/> and no slot drifts over time. Target t = carry + debit x w / sum(w). Part = floor(t) (never below 0).
        /// The units left over (or over-given) go to (come from) the slots with the largest (smallest) fractions. Carry = t - part. The
        /// sum of carries is conserved pass to pass. Weights that are not finite or &lt;= 0 count 0. If sum(w) &lt;= 0 (or count is 0),
        /// the method puts the whole debit in slot <paramref name="fallback"/> (the caller's Not linked slot). It leaves the carries
        /// unchanged. debit &lt;= 0 books nothing. Allocation-free. Worst case O(count^2) (count = ski areas + 1).</summary>
        public static void SplitDebit(long debit, double[] weights, double[] carry, long[] parts, int count, int fallback)
        {
            for (int i = 0; i < count; i++)
                parts[i] = 0;
            if (debit <= 0 || count <= 0)
                return;
            double sum = 0.0;
            for (int i = 0; i < count; i++)
                sum += W(weights[i]);
            if (!(sum > 0.0))
            {
                if (fallback >= 0 && fallback < count)
                    parts[fallback] = debit;
                return;
            }
            long given = 0;
            for (int i = 0; i < count; i++)
            {
                double t = W(weights[i]) > 0.0 ? carry[i] + debit * (W(weights[i]) / sum) : carry[i];
                long p = t > 0.0 ? (long)Math.Floor(t) : 0;
                parts[i] = p;
                carry[i] = t; // the target for now. It becomes t - part below
                given += p;
            }
            while (given < debit)
            {
                int best = -1;
                double bestFrac = double.NegativeInfinity;
                for (int i = 0; i < count; i++)
                {
                    if (!(W(weights[i]) > 0.0))
                        continue;
                    double frac = carry[i] - parts[i];
                    if (frac > bestFrac) { bestFrac = frac; best = i; }
                }
                parts[best]++;
                given++;
            }
            while (given > debit)
            {
                int best = -1;
                double bestFrac = double.PositiveInfinity;
                for (int i = 0; i < count; i++)
                {
                    if (parts[i] <= 0)
                        continue;
                    double frac = carry[i] - parts[i];
                    if (frac < bestFrac) { bestFrac = frac; best = i; }
                }
                parts[best]--;
                given--;
            }
            for (int i = 0; i < count; i++)
                carry[i] -= parts[i];
        }

        private static double W(double w) => double.IsNaN(w) || double.IsInfinity(w) || w <= 0.0 ? 0.0 : w;

        /// <summary>A saved month in progress seeds the ledger only when it is the ledger's month (same key) and was mirrored at all.
        /// The ledger ignores any other key (stale mirror, a save from another month), and counting stays "since load".</summary>
        public static bool ShouldSeed(int ledgerMonthKey, in SkiResortMonthInProgress saved)
            => ledgerMonthKey != SkiResortLedger.NoMonth && saved.m_MonthKey != SkiResortLedger.NoMonth && saved.m_MonthKey == ledgerMonthKey;
    }
}
