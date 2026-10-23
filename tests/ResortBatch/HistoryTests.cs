// Tests of the pure history store (SkiResortHistory.cs) and the ledger's history additions (SkiResortLedger.cs); `Run(check)` is called from
// Program.Main. Cases H-1..H-10. Pinned: the element codec (59 literal bytes, little-endian; the version byte is CurrentVersion whatever
// m_Version holds) and the month-in-progress codec (142 bytes = V1Bytes) round-trip byte for byte; Append orders, dedupes (same key replaces,
// an older key is rejected) and adds one GAP marker per jump; the 1200-month cap drops the oldest; SplitDebit conserves the debit and its
// carries; a closed month's element net == HubRules.NetMonth of the ledger's month == a literal oracle (historyRules.ts finish() derives it
// from the element's terms). Oracles are literal hand values; the byte oracle is our reading of Colossal's BinaryWriter (raw little-endian
// values in call order, BinaryWriter.cs 1.6.2f1).

using System;
using System.Collections.Generic;
using SkiResortMod.Systems;
using H = SkiResortMod.Systems.SkiResortHistory;

namespace ResortBatch
{
    public static class HistoryTests
    {
        private static Func<bool, string, string, bool> s_Check;

        public static void Run(Func<bool, string, string, bool> check)
        {
            s_Check = check;
            ElementCodec();
            ProgressCodec();
            AppendRules();
            Cap();
            Seasons();
            Split();
            LedgerUpkeep();
            Seed();
            Mapping();
            OneNet();
        }

        private static void C(bool ok, string where, string msg) => s_Check(ok, where, msg);


        private struct ByteWriter : IHistoryWriter
        {
            public List<byte> Bytes;
            public void Write(byte v) { Bytes.Add(v); }
            public void Write(int v) { for (int i = 0; i < 4; i++) Bytes.Add((byte)(v >> (8 * i))); }
            public void Write(long v) { for (int i = 0; i < 8; i++) Bytes.Add((byte)(v >> (8 * i))); }
            public void Write(double v) { Write(BitConverter.DoubleToInt64Bits(v)); }
        }

        private struct ByteReader : IHistoryReader
        {
            public List<byte> Bytes; public int Pos;
            public void Read(out byte v) { v = Bytes[Pos++]; }
            public void Read(out int v) { v = 0; for (int i = 0; i < 4; i++) v |= Bytes[Pos++] << (8 * i); }
            public void Read(out long v) { v = 0; for (int i = 0; i < 8; i++) v |= (long)Bytes[Pos++] << (8 * i); }
            public void Read(out double v) { Read(out long b); v = BitConverter.Int64BitsToDouble(b); }
        }

        private struct ListStore : IHistoryStore
        {
            public List<SkiResortHistoryMonth> L;
            public int Count => L.Count;
            public SkiResortHistoryMonth this[int i] { get => L[i]; set => L[i] = value; }
            public void Add(SkiResortHistoryMonth m) { L.Add(m); }
            public void RemoveRange(int index, int count) { L.RemoveRange(index, count); }
        }

        private static ListStore NewStore() => new ListStore { L = new List<SkiResortHistoryMonth>() };

        private static SkiResortHistoryMonth M(int key, int tourists = 0)
        {
            SkiResortHistoryMonth m = SkiResortHistoryMonth.Gap(key);
            m.m_Flags = 0;
            m.m_Tourists = tourists;
            return m;
        }

        private static string Hex(List<byte> b, int from, int n)
        {
            var s = new System.Text.StringBuilder();
            for (int i = from; i < Math.Min(b.Count, from + n); i++)
                s.Append(b[i].ToString("X2")).Append(' ');
            return s.ToString();
        }


        private static void ElementCodec()
        {
            C(SkiResortHistoryMonth.CurrentVersion == 1 && SkiResortHistoryMonth.V1Bytes == 59, "H-1 consts", "version / V1Bytes");
            var m = new SkiResortHistoryMonth
            {
                m_Version = 0,   // not what is written: Serialize always writes CurrentVersion
                m_Flags = HistoryFlags.First | HistoryFlags.Partial,          // 0x41
                m_MonthKey = 2026 * 12 + 2,                                    // 24314 = 0x5EFA
                m_Tourists = 120, m_Residents = 40, m_SkiDays = -1, m_TicketsPaid = 100,
                m_TicketIncome = 1200,                                         // 0x04B0
                m_LodgeIncome = 0, m_PassIncome = 70000,                       // 0x011170
                m_LessonIncome = 300,                                          // 0x012C
                m_LiftCost = 450,                                              // 0x01C2
                m_RunCost = 2000,                                              // 0x07D0
                m_BuildingsCost = -1, m_WagesCost = 3000,                      // 0x0BB8
                m_Attraction = 45, m_SnowPct = 80,
            };
            byte[] want =
            {
                0x01, 0x41,
                0xFA, 0x5E, 0x00, 0x00,   0x78, 0x00, 0x00, 0x00,   0x28, 0x00, 0x00, 0x00,   0xFF, 0xFF, 0xFF, 0xFF,
                0x64, 0x00, 0x00, 0x00,   0xB0, 0x04, 0x00, 0x00,   0x00, 0x00, 0x00, 0x00,   0x70, 0x11, 0x01, 0x00,
                0x2C, 0x01, 0x00, 0x00,   0xC2, 0x01, 0x00, 0x00,   0xD0, 0x07, 0x00, 0x00,   0xFF, 0xFF, 0xFF, 0xFF,
                0xB8, 0x0B, 0x00, 0x00,   0x2D, 0x00, 0x00, 0x00,
                0x50,
            };
            var w = new ByteWriter { Bytes = new List<byte>() };
            m.WriteTo(ref w);
            C(w.Bytes.Count == 59 && want.Length == 59, "H-1 size", $"wrote {w.Bytes.Count} bytes, oracle {want.Length}");
            bool same = w.Bytes.Count == want.Length;
            int at = -1;
            for (int i = 0; same && i < want.Length; i++)
                if (w.Bytes[i] != want[i]) { same = false; at = i; }
            C(same, "H-1 bytes", at < 0 ? "length" : $"byte {at}: {Hex(w.Bytes, Math.Max(0, at - 2), 6)}");

            var r = new ByteReader { Bytes = w.Bytes };
            var back = default(SkiResortHistoryMonth);
            back.ReadFrom(ref r);
            C(r.Pos == 59, "H-1 read", "read " + r.Pos + " bytes");
            C(back.m_Version == 1 && back.m_Flags == 0x41 && back.m_MonthKey == 24314 && back.m_Tourists == 120 && back.m_Residents == 40 &&
              back.m_SkiDays == -1 && back.m_TicketsPaid == 100 && back.m_TicketIncome == 1200 && back.m_LodgeIncome == 0 &&
              back.m_PassIncome == 70000 && back.m_LessonIncome == 300 && back.m_LiftCost == 450 && back.m_RunCost == 2000 &&
              back.m_BuildingsCost == -1 && back.m_WagesCost == 3000 && back.m_Attraction == 45 && back.m_SnowPct == 80,
              "H-1 fields", "a field did not come back");
            var w2 = new ByteWriter { Bytes = new List<byte>() };
            back.WriteTo(ref w2);
            bool again = w2.Bytes.Count == w.Bytes.Count;
            for (int i = 0; again && i < w.Bytes.Count; i++)
                again = w.Bytes[i] == w2.Bytes[i];
            C(again, "H-1 rewrite", "read-then-write is not byte-identical");
        }


        private static void ProgressCodec()
        {
            C(SkiResortMonthInProgress.V1Bytes == 142, "H-MP1 consts", "V1Bytes = " + SkiResortMonthInProgress.V1Bytes + " (want 142)");
            // The month-in-progress codec copies Ledger.Month's fields BY NAME (SkiResortHistory.cs), so a 20th public field would silently
            // not persist or seed while H-MP1's 142 bytes still pass. The saved SkiResortMonthInProgress / SkiResortHistoryMonth bytes carry
            // exactly these 19 fields; adding one means a NEW ISerializable type (SaveLayouts.cs), never a change to the existing layouts.
            int monthFields = typeof(SkiResortLedger.Month).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance).Length;
            C(monthFields == 19, "H-MP1 Month fields", "SkiResortLedger.Month has " + monthFields + " public instance fields (want exactly 19): " +
              "the history element and month-in-progress codecs copy them by name, so a new field is NOT persisted or seeded until a NEW " +
              "ISerializable type carries it (save layout frozen after 1.0; see SaveLayouts.cs)");
            var month = new SkiResortLedger.Month
            {
                TouristGuests = 1, TouristParties = 2, LocalGuests = 3, TouristOnLeisure = 4, TicketsPaid = 5, TicketsUnpaid = 6,
                TicketsFree = 7, TicketIncome = 8_000_000_000L, LiftCharged = 123.456, LodgeSpends = 9, LodgeSpendIncome = 10,
                PassesSold = 11, PassIncome = 12, PassHolderGuests = 13, PassesPre = 14, PassIncomePre = 15, Lessons = 16,
                LessonIncome = 17, RunUpkeep = 18,
            };
            var e = new SkiResortLedger.Entry
            {
                This = month, PassSeasonKey = 2025, PassSeasonSold = 19, PassSeasonIncome = 20, PassSeasonPre = 21, PassSeasonPreIncome = 22,
            };
            SkiResortMonthInProgress p = SkiResortMonthInProgress.From(e, 24314, partial: true, byJump: true, framesSimulated: 100_000u);
            C(p.m_Flags == (SkiResortMonthInProgress.FlagPartial | SkiResortMonthInProgress.FlagByJump), "H-MP1 flags", "flags " + p.m_Flags);
            C(SkiResortMonthInProgress.From(e, 1, false, true, 0).m_Flags == 0, "H-MP1 flags", "by-jump without partial is set");
            C(SkiResortMonthInProgress.From(e, 1, false, false, uint.MaxValue).m_FramesSimulated == int.MaxValue, "H-MP1 frames", "clamp");
            var w = new ByteWriter { Bytes = new List<byte>() };
            p.WriteTo(ref w);
            C(w.Bytes.Count == 142, "H-MP1 size", "wrote " + w.Bytes.Count);
            C(w.Bytes[0] == 1 && w.Bytes[1] == 3 && w.Bytes[2] == 0xFA && w.Bytes[3] == 0x5E, "H-MP1 head", Hex(w.Bytes, 0, 6));
            var r = new ByteReader { Bytes = w.Bytes };
            var b = default(SkiResortMonthInProgress);
            b.ReadFrom(ref r);
            C(r.Pos == 142, "H-MP1 read", "read " + r.Pos);
            SkiResortLedger.Month m = b.m_Month;
            C(b.m_Version == 1 && b.m_MonthKey == 24314 && b.m_FramesSimulated == 100_000 &&
              m.TouristGuests == 1 && m.TouristParties == 2 && m.LocalGuests == 3 && m.TouristOnLeisure == 4 && m.TicketsPaid == 5 &&
              m.TicketsUnpaid == 6 && m.TicketsFree == 7 && m.TicketIncome == 8_000_000_000L &&
              BitConverter.DoubleToInt64Bits(m.LiftCharged) == BitConverter.DoubleToInt64Bits(123.456) && m.LodgeSpends == 9 &&
              m.LodgeSpendIncome == 10 && m.PassesSold == 11 && m.PassIncome == 12 && m.PassHolderGuests == 13 && m.PassesPre == 14 &&
              m.PassIncomePre == 15 && m.Lessons == 16 && m.LessonIncome == 17 && m.RunUpkeep == 18 &&
              b.m_PassSeasonKey == 2025 && b.m_PassSeasonSold == 19 && b.m_PassSeasonIncome == 20 && b.m_PassSeasonPre == 21 &&
              b.m_PassSeasonPreIncome == 22, "H-MP1 fields", "a field did not come back");
            var w2 = new ByteWriter { Bytes = new List<byte>() };
            b.WriteTo(ref w2);
            bool same = w2.Bytes.Count == 142;
            for (int i = 0; same && i < 142; i++)
                same = w.Bytes[i] == w2.Bytes[i];
            C(same, "H-MP1 rewrite", "not byte-identical");
        }


        private static void AppendRules()
        {
            ListStore s = NewStore();
            C(H.LastKey(ref s) == SkiResortLedger.NoMonth, "H-3 empty", "LastKey of empty");
            C(H.Append(ref s, M(100, 1)) == H.AppendResult.Appended && s.Count == 1, "H-3 first", "count " + s.Count);
            C(H.Append(ref s, M(101, 2)) == H.AppendResult.Appended && s.Count == 2, "H-3 next", "consecutive month added a marker?");
            C(H.Append(ref s, M(101, 7)) == H.AppendResult.Replaced && s.Count == 2 && s[1].m_Tourists == 7, "H-3 dedupe",
              "same key: count " + s.Count + ", tourists " + s[1].m_Tourists);
            C(H.Append(ref s, M(99, 9)) == H.AppendResult.Rejected && s.Count == 2 && s[0].m_MonthKey == 100, "H-3 older", "older key stored");
            // jump 101 → 105: one marker at 102 (covers 102..104), then 105
            C(H.Append(ref s, M(105, 5)) == H.AppendResult.AppendedAfterGap && s.Count == 4, "H-3 gap", "count " + s.Count);
            C(s[2].IsGap && s[2].m_MonthKey == 102 && s[2].m_SkiDays == -1 && s[2].m_BuildingsCost == -1 && s[2].m_SnowPct == 255 &&
              s[2].m_Tourists == 0, "H-3 gap", "marker fields");
            C(!s[3].IsGap && s[3].m_MonthKey == 105 && H.LastKey(ref s) == 105, "H-3 gap", "after the marker");
            C(H.Append(ref s, M(106)) == H.AppendResult.Appended && s.Count == 5, "H-3 after gap", "count " + s.Count);
            // keys strictly increasing, markers included
            bool inc = true;
            for (int i = 1; i < s.Count; i++)
                inc &= s[i].m_MonthKey > s[i - 1].m_MonthKey;
            C(inc, "H-3 order", "keys not strictly increasing");
            // a stored marker as the last element: data at a later key appends without a second marker; at its key replaces it
            ListStore g = NewStore();
            g.Add(M(10));
            g.Add(SkiResortHistoryMonth.Gap(11));
            C(H.Append(ref g, M(14)) == H.AppendResult.AppendedAfterGap && g.Count == 3, "H-3 marker last", "count " + g.Count);
            ListStore g2 = NewStore();
            g2.Add(M(10));
            g2.Add(SkiResortHistoryMonth.Gap(11));
            C(H.Append(ref g2, M(11, 3)) == H.AppendResult.Replaced && g2.Count == 2 && !g2[1].IsGap, "H-3 marker replaced", "marker kept");
        }


        private static void Cap()
        {
            C(H.CapMonths == 1200, "H-4 const", "cap " + H.CapMonths);
            ListStore s = NewStore();
            for (int k = 0; k < 1205; k++)
                H.Append(ref s, M(1000 + k, k));
            C(s.Count == 1200 && s[0].m_MonthKey == 1005 && s[1199].m_MonthKey == 2204, "H-4 cap",
              $"count {s.Count}, first {s[0].m_MonthKey}, last {s[s.Count - 1].m_MonthKey}");
            // 1199 data months then a jump: [d0..d1198, gap, d] = 1201 → drop 1 → d1 first (not a marker)
            ListStore t = NewStore();
            for (int k = 0; k < 1199; k++)
                H.Append(ref t, M(k));
            H.Append(ref t, M(5000));
            C(t.Count == 1200 && t[0].m_MonthKey == 1 && t[1198].IsGap && t[1199].m_MonthKey == 5000, "H-4 cap gap",
              $"count {t.Count}, first {t[0].m_MonthKey}");
            // a marker that would lead the store is dropped with the month before it
            ListStore u = NewStore();
            u.Add(M(1));
            u.Add(SkiResortHistoryMonth.Gap(2));
            u.Add(M(9));
            u.Add(M(10));
            int dropped = H.Trim(ref u, 3);
            C(dropped == 2 && u.Count == 2 && u[0].m_MonthKey == 9, "H-4 leading marker", $"dropped {dropped}, first {u[0].m_MonthKey}");
        }


        private static void Seasons()
        {
            // month index 0..11 of the key; MonthOfKey 1..12. June 2026 = 2026*12+5, July 2026 = 2026*12+6.
            C(H.SeasonOf(2026 * 12 + 5) == 2025 && H.SeasonOf(2026 * 12 + 6) == 2026 && H.SeasonOf(2025 * 12 + 11) == 2025 &&
              H.SeasonOf(2026 * 12 + 0) == 2025, "H-5 SeasonOf", "June / July / December / January");
            C(H.SeasonFirstKey(2025) == 2025 * 12 + 6, "H-5 first key", "July 2025 = " + H.SeasonFirstKey(2025));
            // 2025-05 .. 2026-08 (16 months): tourists = month number so sums are checkable
            ListStore s = NewStore();
            for (int k = 2025 * 12 + 4; k <= 2026 * 12 + 7; k++)
            {
                SkiResortHistoryMonth m = M(k, 10);
                m.m_Residents = 1;
                m.m_TicketIncome = 100;
                m.m_RunCost = 7;
                m.m_SkiDays = k == 2025 * 12 + 8 ? -1 : 2;       // September 2025 unknown
                m.m_Attraction = k == 2025 * 12 + 9 ? -1 : 40;   // October 2025 unknown
                m.m_SnowPct = (byte)(k == 2025 * 12 + 10 ? 255 : 50);
                m.m_BuildingsCost = 1000;
                m.m_WagesCost = -1;
                if (k == 2026 * 12 + 0)
                    m.m_Flags = HistoryFlags.Partial;
                H.Append(ref s, m);
            }
            H.Bucket b = H.SumSeason(ref s, 2025);   // July 2025 .. June 2026 = 12 months
            C(b.Months == 12 && b.Tourists == 120 && b.Residents == 12 && b.Guests == 132 && b.TicketIncome == 1200 && b.Income == 1200 &&
              b.RunCost == 84, "H-5 season sums", $"months {b.Months}, tourists {b.Tourists}, ticket {b.TicketIncome}, run {b.RunCost}");
            C(b.SkiDaysMonths == 11 && b.SkiDays == 22, "H-5 unknown ski days", $"{b.SkiDays} over {b.SkiDaysMonths}");
            C(b.Attraction == 40f && b.SnowPct == 50f, "H-5 point means", $"A {b.Attraction}, snow {b.SnowPct}");
            C(b.BuildingsMonths == 12 && b.BuildingsCost == 12000 && b.WagesMonths == 0, "H-5 rates", "buildings / wages known months");
            C(b.Flags == HistoryFlags.Partial, "H-5 flags", "OR of flags " + b.Flags);
            H.Bucket next = H.SumSeason(ref s, 2026);   // July, August 2026 only
            C(next.Months == 2 && next.Tourists == 20, "H-5 July boundary", $"season 2026: {next.Months} months");
            H.Bucket prev = H.SumSeason(ref s, 2024);   // May, June 2025 only
            C(prev.Months == 2 && prev.Tourists == 20, "H-5 June boundary", $"season 2024: {prev.Months} months");
            // a gap inside a season is skipped (not a zero month)
            ListStore g = NewStore();
            H.Append(ref g, M(2025 * 12 + 6, 5));
            H.Append(ref g, M(2025 * 12 + 9, 5));   // marker at August
            H.Bucket gb = H.SumSeason(ref g, 2025);
            C(g.Count == 3 && gb.Months == 2 && gb.Tourists == 10 && (gb.Flags & HistoryFlags.Gap) == 0 && gb.Attraction == -1f,
              "H-5 gap skipped", $"count {g.Count}, months {gb.Months}, flags {gb.Flags}");
        }


        private static void Split()
        {
            var w = new double[4];
            var c = new double[4];
            var p = new long[4];
            // three areas 500 / 300 / 200 + Not linked 0; debit 10 → 5 / 3 / 2 / 0 exactly
            w[0] = 500; w[1] = 300; w[2] = 200; w[3] = 0;
            H.SplitDebit(10, w, c, p, 4, 3);
            C(p[0] == 5 && p[1] == 3 && p[2] == 2 && p[3] == 0, "H-6 exact", $"{p[0]}/{p[1]}/{p[2]}/{p[3]}");
            // 7 over thirds: 7/3 = 2.33 each → floors 2+2+2 = 6, one more unit to the largest fraction (ties: the first)
            Array.Clear(c, 0, 4);
            w[0] = 1; w[1] = 1; w[2] = 1; w[3] = 0;
            H.SplitDebit(7, w, c, p, 4, 3);
            C(p[0] + p[1] + p[2] + p[3] == 7 && p[3] == 0 && Math.Abs(c[0] + c[1] + c[2] + c[3]) < 1e-9, "H-6 thirds",
              $"{p[0]}/{p[1]}/{p[2]}, carry sum {c[0] + c[1] + c[2]}");
            // drift: weights 3 % / 97 %, 1000 passes of 20 → 600 / 19,400 within one credit, every pass exact
            Array.Clear(c, 0, 4);
            w[0] = 0.03; w[1] = 0.97;
            long a = 0, b = 0;
            bool everyExact = true;
            for (int i = 0; i < 1000; i++)
            {
                H.SplitDebit(20, w, c, p, 2, 1);
                everyExact &= p[0] + p[1] == 20 && p[0] >= 0 && p[1] >= 0;
                a += p[0];
                b += p[1];
            }
            C(everyExact && a + b == 20000, "H-6 sum", "a pass did not sum to its debit");
            C(Math.Abs(a - 600) <= 1 && Math.Abs(c[0]) < 1.0 && Math.Abs(c[0] + c[1]) < 1e-6, "H-6 no drift",
              $"small area {a} (want 600 ± 1), carry {c[0]:F3}, carry sum {c[0] + c[1]:E2}");
            // nothing to weigh → the fallback slot takes it all; bad weights count 0
            Array.Clear(c, 0, 4);
            w[0] = 0; w[1] = double.NaN; w[2] = -5; w[3] = 0;
            H.SplitDebit(9, w, c, p, 4, 3);
            C(p[3] == 9 && p[0] == 0 && p[1] == 0 && p[2] == 0, "H-6 fallback", $"{p[0]}/{p[1]}/{p[2]}/{p[3]}");
            w[0] = double.PositiveInfinity; w[1] = 1; w[2] = 0; w[3] = 1;
            H.SplitDebit(4, w, c, p, 4, 3);
            C(p[0] == 0 && p[1] == 2 && p[3] == 2, "H-6 infinity", $"{p[0]}/{p[1]}/{p[2]}/{p[3]}");
            H.SplitDebit(0, w, c, p, 4, 3);
            C(p[0] == 0 && p[1] == 0 && p[2] == 0 && p[3] == 0, "H-6 zero debit", "something booked");
            // a negative carry (an area that got a unit early) never makes a part negative, and the sum stays exact
            w[0] = 1; w[1] = 1; w[2] = 0; w[3] = 0;
            c[0] = -0.9; c[1] = 0.9; c[2] = 0; c[3] = 0;
            H.SplitDebit(1, w, c, p, 4, 3);
            C(p[0] >= 0 && p[1] >= 0 && p[0] + p[1] == 1 && p[1] == 1, "H-6 negative carry", $"{p[0]}/{p[1]}");
        }


        private static void LedgerUpkeep()
        {
            var l = new SkiResortLedger();
            var a = new EntityRef(10, 1);
            l.Advance(500);
            int rev = l.Revision;
            l.AddRunUpkeep(a, 0);
            l.AddRunUpkeep(a, -3);
            C(l.Count == 0 && l.Revision == rev, "H-7 ignored", "≤ 0 created an entry or moved Revision");
            l.AddRunUpkeep(a, 120);
            l.AddRunUpkeep(EntityRef.Null, 30);
            l.AddRunUpkeep(a, 5);
            C(l.TryGet(a, out SkiResortLedger.Entry e) && e.This.RunUpkeep == 125 && l.Total(false).RunUpkeep == 155, "H-7 booked",
              "this " + e.This.RunUpkeep + ", total " + l.Total(false).RunUpkeep);
            l.Advance(501);
            C(l.TryGet(a, out e) && e.Last.RunUpkeep == 125 && e.This.RunUpkeep == 0 && l.Total(true).RunUpkeep == 155, "H-7 roll",
              "last " + e.Last.RunUpkeep);
        }


        private static void Seed()
        {
            var a = new EntityRef(10, 1);
            var b = new EntityRef(11, 1);
            var saved = new SkiResortLedger.Month { TouristGuests = 50, LocalGuests = 20, TicketIncome = 900, LiftCharged = 12.5, RunUpkeep = 300 };
            var savedP = new SkiResortMonthInProgress { m_MonthKey = 700, m_Month = saved };

            // not started: nothing
            var l0 = new SkiResortLedger();
            C(!l0.SeedMonth(700, false) && !H.ShouldSeed(l0.MonthKey, savedP), "H-8 not started", "seeded before the first Advance");

            // same key: seeded, whole, added to what was booked since the load
            var l = new SkiResortLedger();
            l.Advance(700);   // Started: since load
            C(l.ThisMonthSinceLoad, "H-8 start", "a start is not since-load");
            l.AddArrival(a, tourist: true, people: 4);
            C(H.ShouldSeed(l.MonthKey, savedP), "H-8 ShouldSeed", "same key refused");
            int rev = l.Revision;
            C(l.SeedMonth(700, partial: false) && !l.ThisMonthSinceLoad && l.Revision != rev, "H-8 seed month", "not whole after the seed");
            l.SeedThis(a, saved, 2025, 3, 600, 1, 150);
            C(l.TryGet(a, out SkiResortLedger.Entry e) && e.This.TouristGuests == 54 && e.This.LocalGuests == 20 &&
              e.This.TicketIncome == 900 && e.This.LiftCharged == 12.5 && e.This.RunUpkeep == 300, "H-8 seed this",
              $"tourists {e.This.TouristGuests}, run {e.This.RunUpkeep}");
            C(SkiResortLedger.PassSeasonSoldOf(e, 2025) == 3 && SkiResortLedger.PassSeasonIncomeOf(e, 2025) == 600 &&
              SkiResortLedger.PassSeasonPreOf(e, 2025) == 1 && SkiResortLedger.PassSeasonPreIncomeOf(e, 2025) == 150, "H-8 season taken",
              "pass season not taken into an empty entry");
            // same season again adds; another season is ignored once this one has sales
            l.SeedThis(a, default, 2025, 2, 400, 0, 0);
            l.SeedThis(a, default, 2026, 9, 900, 0, 0);
            C(l.TryGet(a, out e) && SkiResortLedger.PassSeasonSoldOf(e, 2025) == 5 && SkiResortLedger.PassSeasonIncomeOf(e, 2025) == 1000 &&
              SkiResortLedger.PassSeasonSoldOf(e, 2026) == 0, "H-8 season merge", "sold 2025 " + SkiResortLedger.PassSeasonSoldOf(e, 2025));
            // an all-zero month creates no entry
            int count = l.Count;
            l.SeedThis(b, default, SkiResortLedger.NoSeason, 0, 0, 0, 0);
            C(l.Count == count, "H-8 zero", "an empty seed created an entry");
            // the roll after a whole seed closes a full month
            l.Advance(701);
            C(l.LastMonthKnown && !l.LastMonthSinceLoad && l.TryGet(a, out e) && e.Last.TouristGuests == 54, "H-8 roll after seed",
              $"last known {l.LastMonthKnown}, partial {l.LastMonthSinceLoad}");

            // a saved partial month stays partial
            var lp = new SkiResortLedger();
            lp.Advance(700);
            C(lp.SeedMonth(700, partial: true) && lp.ThisMonthSinceLoad, "H-8 partial kept", "a partial save became whole");

            // other key: nothing changes, counting stays since load
            var lm = new SkiResortLedger();
            lm.Advance(702);
            rev = lm.Revision;
            C(!H.ShouldSeed(lm.MonthKey, savedP) && !lm.SeedMonth(700, false) && lm.ThisMonthSinceLoad && lm.Revision == rev &&
              lm.Count == 0, "H-8 mismatch", "a stale month was seeded");
            var noMonth = new SkiResortMonthInProgress { m_MonthKey = SkiResortLedger.NoMonth };
            C(!H.ShouldSeed(702, noMonth), "H-8 never mirrored", "NoMonth seeded");
        }


        private static void Mapping()
        {
            var m = new SkiResortLedger.Month
            {
                TouristGuests = 7, LocalGuests = 3, TicketsPaid = 6, TicketIncome = 3_000_000_000L, LodgeSpendIncome = 11,
                PassIncome = 12, LessonIncome = 13, LiftCharged = 449.5, RunUpkeep = 99,
            };
            SkiResortHistoryMonth h = SkiResortHistoryMonth.FromLedger(m, 24000, HistoryFlags.Short);
            C(h.m_Version == 1 && h.m_Flags == HistoryFlags.Short && h.m_MonthKey == 24000 && h.m_Tourists == 7 && h.m_Residents == 3 &&
              h.m_TicketsPaid == 6 && h.m_TicketIncome == int.MaxValue && h.m_LodgeIncome == 11 && h.m_PassIncome == 12 &&
              h.m_LessonIncome == 13 && h.m_LiftCost == 450 && h.m_RunCost == 99, "H-9 FromLedger", $"ticket {h.m_TicketIncome}, lift {h.m_LiftCost}");
            C(h.m_SkiDays == -1 && h.m_BuildingsCost == -1 && h.m_WagesCost == -1 && h.m_Attraction == -1 && h.m_SnowPct == 255,
              "H-9 unknowns", "snapshot fields not unknown");
            C(H.RoundMoney(double.NaN) == 0 && H.RoundMoney(-2.5) == -3 && H.RoundMoney(1e12) == int.MaxValue && H.ClampInt(long.MinValue) == int.MinValue,
              "H-9 money", "RoundMoney / ClampInt");
            C(SkiResortHistoryMonth.SnowByte(79.5f) == 80 && SkiResortHistoryMonth.SnowByte(0f) == 0 && SkiResortHistoryMonth.SnowByte(120f) == 100 &&
              SkiResortHistoryMonth.SnowByte(-1f) == 255 && SkiResortHistoryMonth.SnowByte(float.NaN) == 255, "H-9 snow", "SnowByte");
        }


        /// <summary>historyRules.ts finish() for one month, transcribed: income = ticket + lodge + pass + lesson; staff = max(0, b) +
        /// max(0, w) when either is known, else −1; costs = max(0, lift) + max(0, run) + max(0, staff); net = income − costs.</summary>
        private static long TsNet(in SkiResortHistoryMonth m)
        {
            long income = (long)m.m_TicketIncome + m.m_LodgeIncome + m.m_PassIncome + m.m_LessonIncome;
            int b = m.m_BuildingsCost, w = m.m_WagesCost;
            long staff = b >= 0 || w >= 0 ? (long)Math.Max(0, b) + Math.Max(0, w) : -1;
            long costs = Math.Max(0, m.m_LiftCost) + (long)Math.Max(0, m.m_RunCost) + Math.Max(0L, staff);
            return income - costs;
        }

        private static void OneNet()
        {
            var a = new EntityRef(7, 1);
            var l = new SkiResortLedger();
            l.Advance(700);
            l.AddTickets(a, paid: 3, unpaid: 1, free: 0, income: 900);
            l.AddLodgeSpend(a, 2, 150);
            l.AddLessons(a, 2, 200);
            l.AddPasses(a, 3, 600, 0, 2025, preSeason: true);   // the pre-season sale: in PassIncome once, as a subset
            l.AddPasses(a, 2, 400, 5, 2025);
            l.AddLiftCharge(a, 449.5);
            l.AddRunUpkeep(a, 99);
            C(l.Advance(701) == SkiResortLedger.AdvanceResult.Rolled && l.TryGet(a, out SkiResortLedger.Entry e), "H-10 setup", "no roll");
            l.TryGet(a, out e);
            SkiResortLedger.Month closed = e.Last;

            // the element as SkiResortHistorySystem.Commit stores it: FromLedger + the rates at the roll
            SkiResortHistoryMonth el = SkiResortHistoryMonth.FromLedger(closed, 700, 0);
            el.m_BuildingsCost = 6000;
            el.m_WagesCost = 15000;
            HubMonthMoney fromLedger = HubRules.MonthMoneyOf(closed, 6000, 15000);
            HubMonthMoney fromElement = el.Money;
            C(fromLedger.TicketIncome == fromElement.TicketIncome && fromLedger.LodgeIncome == fromElement.LodgeIncome &&
              fromLedger.LessonIncome == fromElement.LessonIncome && fromLedger.PassIncome == fromElement.PassIncome &&
              fromLedger.LiftCost == fromElement.LiftCost && fromLedger.RunCost == fromElement.RunCost &&
              fromLedger.BuildingsCost == fromElement.BuildingsCost && fromLedger.WagesCost == fromElement.WagesCost, "H-10 terms",
              $"element pass {fromElement.PassIncome} / ledger {fromLedger.PassIncome}, lift {fromElement.LiftCost} / {fromLedger.LiftCost}");
            // 900 + 150 + 200 + 1000 = 2,250 income; 450 + 99 + 6,000 + 15,000 = 21,549 costs → −19,299
            int net = HubRules.NetMonth(fromLedger, out bool est);
            C(net == -19299 && !est && HubRules.NetMonth(fromElement, out bool _) == net && TsNet(el) == net, "H-10 net",
              $"ledger NetMonth {net} (est {est}), element {HubRules.NetMonth(fromElement, out bool _)}, TS {TsNet(el)}; want −19299");

            // snapshot missing (both rates unknown) and one rate unknown: C# reads −1 as 0, TS leaves staff out — the same net
            el.m_BuildingsCost = SkiResortHistoryMonth.Unknown;
            el.m_WagesCost = SkiResortHistoryMonth.Unknown;
            int netMissing = HubRules.NetMonth(el.Money, out bool estMissing);
            C(netMissing == 2250 - 549 && estMissing && TsNet(el) == netMissing &&
              HubRules.NetMonth(HubRules.MonthMoneyOf(closed, -1, -1), out bool _) == netMissing, "H-10 snapshot missing",
              $"C# {netMissing} (est {estMissing}), TS {TsNet(el)}; want 1701 ≈");
            el.m_BuildingsCost = 6000;
            C(HubRules.NetMonth(el.Money, out bool _) == 2250 - 549 - 6000 && TsNet(el) == 2250 - 549 - 6000, "H-10 one rate",
              $"C# {HubRules.NetMonth(el.Money, out bool _)}, TS {TsNet(el)}");

            // a Gap marker nets 0 (nothing booked, bill unknown) in both
            SkiResortHistoryMonth gap = SkiResortHistoryMonth.Gap(702);
            C(HubRules.NetMonth(gap.Money, out bool _) == 0 && TsNet(gap) == 0, "H-10 gap", "a gap marker does not net 0");
        }
    }
}
