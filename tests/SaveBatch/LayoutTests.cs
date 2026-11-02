// SL-T (the table) and SL-L (each persisted type against it). See the Program.cs header.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Colossal.Serialization.Entities;
using SkiResortMod.Systems;
using Unity.Entities;

namespace SaveBatch
{
    /// <summary>Colossal BinaryWriter's encoding (header of Program.cs) for both seams: the shim IWriter (the three component
    /// files) and IHistoryWriter (the history codec). A class, so a by-value Serialize(writer) still appends here.</summary>
    internal sealed class ByteWriter : IWriter, IHistoryWriter
    {
        public readonly List<byte> Bytes = new List<byte>();
        public void Write(byte v) { Bytes.Add(v); }
        public void Write(int v) { for (int i = 0; i < 4; i++) Bytes.Add((byte)(v >> (8 * i))); }
        public void Write(long v) { for (int i = 0; i < 8; i++) Bytes.Add((byte)(v >> (8 * i))); }
        public void Write(double v) { Write(BitConverter.DoubleToInt64Bits(v)); }
        public void Write(Entity v) { Write(v.Index); }   // identity entity table (EngineShims header)
    }

    internal sealed class ByteReader : IReader, IHistoryReader
    {
        private readonly List<byte> m_Bytes;
        public int Pos;
        public ByteReader(List<byte> bytes) { m_Bytes = bytes; }
        private byte Next()
        {
            if (Pos >= m_Bytes.Count)
                throw new EndOfStreamException("read past the block (" + m_Bytes.Count + " bytes)");
            return m_Bytes[Pos++];
        }
        public void Read(out byte v) { v = Next(); }
        public void Read(out int v) { v = 0; for (int i = 0; i < 4; i++) v |= Next() << (8 * i); }
        public void Read(out long v) { v = 0; for (int i = 0; i < 8; i++) v |= (long)Next() << (8 * i); }
        public void Read(out double v) { Read(out long b); v = BitConverter.Int64BitsToDouble(b); }
        public void Read(out Entity v) { Read(out int i); v = i < 0 ? Entity.Null : new Entity { Index = i, Version = 1 }; }
    }

    public static class LayoutTests
    {
        private static Func<bool, string, string, bool> s_Check;
        private static bool C(bool ok, string where, string msg) => s_Check(ok, where, msg);

        // Shipped snapshot: every table entry that is (or will be) in players' saves, verbatim as Render() prints it. A guard copy,
        // not a second table: SL-T fails if a shipped entry is edited or removed; a new entry (a post-release bump) is appended to
        // SaveLayouts.cs and listed as INFO until it ships, then added here.
        private static readonly string[] Shipped =
        {
            "SkiRunBinding v1 10 m_Gate:entity m_Flags:u8 m_PaidAmount:i32",
            "SkiRunTreeMember v1 6 m_Run:entity m_Layer:u8",
            "SkiRunTreeMember v2 8 m_Variant:u8 m_PlanGen:u8",
            "SkiSeasonPass v1 10 m_SeasonKey:i32 m_PricePaid:i32 m_Flags:u8",
            "SkiResortHistoryMonth v1 59 m_Flags:u8 m_MonthKey:i32 m_Tourists:i32 m_Residents:i32 m_SkiDays:i32 m_TicketsPaid:i32 " +
                "m_TicketIncome:i32 m_LodgeIncome:i32 m_PassIncome:i32 m_LessonIncome:i32 m_LiftCost:i32 m_RunCost:i32 " +
                "m_BuildingsCost:i32 m_WagesCost:i32 m_Attraction:i32 m_SnowPct:u8",
            // T142 B7b (2026-10-24, before 1.0): the snow depth per month. Ships in 1.0.0, so it is listed as shipped.
            "SkiResortHistoryMonth v2 63 m_SnowDepthCm:i32",
            "SkiResortMonthInProgress v1 142 m_Flags:u8 m_MonthKey:i32 m_FramesSimulated:i32 m_Month.TouristGuests:i32 " +
                "m_Month.TouristParties:i32 m_Month.LocalGuests:i32 m_Month.TouristOnLeisure:i32 m_Month.TicketsPaid:i32 " +
                "m_Month.TicketsUnpaid:i32 m_Month.TicketsFree:i32 m_Month.TicketIncome:i64 m_Month.LiftCharged:f64 " +
                "m_Month.LodgeSpends:i32 m_Month.LodgeSpendIncome:i64 m_Month.PassesSold:i32 m_Month.PassIncome:i64 " +
                "m_Month.PassHolderGuests:i32 m_Month.PassesPre:i32 m_Month.PassIncomePre:i64 m_Month.Lessons:i32 " +
                "m_Month.LessonIncome:i64 m_Month.RunUpkeep:i64 m_PassSeasonKey:i32 m_PassSeasonSold:i32 " +
                "m_PassSeasonIncome:i64 m_PassSeasonPre:i32 m_PassSeasonPreIncome:i64",
            // The ski bus depot's coach bill (before 1.0). Ships in 1.0.0, so it is listed as shipped.
            "SkiResortMonthInProgress v2 150 m_Month.BusCharged:f64",
            // The run prop placer's tag and per-run spacings (before 1.0). Ship in 1.0.0, so they are listed as shipped.
            "SkiRunPropMember v1 6 m_Run:entity m_Class:u8",
            "SkiRunPropSettings v1 13 m_GunSpacingM:i32 m_LightSpacingM:i32 m_AppliedKey:i32",
            // The lodge number per ski area and tier (before 1.0). Ships in 1.0.0, so it is listed as shipped.
            "SkiLodgeNumber v1 9 m_Sign:entity m_Number:i32",
        };

        public static void Run(Func<bool, string, string, bool> check)
        {
            s_Check = check;
            Table();
            foreach (SaveLayouts.Layout row in SaveLayouts.All)
                OneType(row);
        }


        private static int SizeOf(string kind)
        {
            switch (kind)
            {
                case "u8": return 1;
                case "i32": case "entity": return 4;
                case "i64": case "f64": return 8;
                default: return -1;
            }
        }

        private static Type ClrOf(string kind)
        {
            switch (kind)
            {
                case "u8": return typeof(byte);
                case "i32": return typeof(int);
                case "i64": return typeof(long);
                case "f64": return typeof(double);
                case "entity": return typeof(Entity);
                default: return null;
            }
        }

        /// <summary>A distinct, byte-asymmetric value per field index (an order swap or a retype moves a visible value).</summary>
        private static object Sentinel(string kind, int i)
        {
            switch (kind)
            {
                case "u8": return (byte)(0x40 + i);
                case "i32": return unchecked((int)0x81020300u) + i;
                case "i64": return unchecked((long)0x8877665544332200UL) + i;
                case "f64": return -12345.5 - i;
                case "entity": return new Entity { Index = 7000 + i, Version = 1 };
                default: return null;
            }
        }

        private static void Encode(ByteWriter w, string kind, object v)
        {
            switch (kind)
            {
                case "u8": w.Write((byte)v); break;
                case "i32": w.Write((int)v); break;
                case "i64": w.Write((long)v); break;
                case "f64": w.Write((double)v); break;
                case "entity": w.Write((Entity)v); break;
            }
        }

        private static object Decode(ByteReader r, string kind)
        {
            switch (kind)
            {
                case "u8": { r.Read(out byte v); return v; }
                case "i32": { r.Read(out int v); return v; }
                case "i64": { r.Read(out long v); return v; }
                case "f64": { r.Read(out double v); return v; }
                case "entity": { r.Read(out Entity v); return v; }
                default: return null;
            }
        }

        private struct Field { public string Path; public string Kind; public byte Version; }

        /// <summary>The fields of versions 1..upTo in write order (after the version byte).</summary>
        private static List<Field> Fields(SaveLayouts.Layout row, int upTo)
        {
            var list = new List<Field>();
            foreach (SaveLayouts.LayoutVersion v in row.Versions)
            {
                if (v.Number > upTo)
                    break;
                foreach (string tok in v.Appended.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    int c = tok.LastIndexOf(':');
                    list.Add(new Field { Path = c < 0 ? tok : tok.Substring(0, c), Kind = c < 0 ? "?" : tok.Substring(c + 1), Version = v.Number });
                }
            }
            return list;
        }

        private static string Render(SaveLayouts.Layout row, SaveLayouts.LayoutVersion v) =>
            row.TypeName + " v" + v.Number + " " + v.Bytes + " " + string.Join(" ", v.Appended.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));

        // SL-T

        private static void Table()
        {
            var names = new HashSet<string>();
            var shorts = new HashSet<string>();
            var rendered = new HashSet<string>();
            foreach (SaveLayouts.Layout row in SaveLayouts.All)
            {
                string w = "SL-T " + row.TypeName;
                C(names.Add(row.TypeName), w, "type listed twice");
                C(shorts.Add(row.Short), w, "short name '" + row.Short + "' used twice (the changelog line would be ambiguous)");
                C(row.Kind == SaveLayouts.KindComponent || row.Kind == SaveLayouts.KindBuffer, w, "kind '" + row.Kind + "'");
                C(row.Versions.Length > 0, w, "no version entry");
                int prevBytes = 1;
                var seen = new HashSet<string>();
                for (int k = 0; k < row.Versions.Length; k++)
                {
                    SaveLayouts.LayoutVersion v = row.Versions[k];
                    C(v.Number == k + 1, w, "entry " + k + " is v" + v.Number + " (versions are 1..n, consecutive)");
                    int sum = 1;
                    foreach (Field f in Fields(row, v.Number))
                    {
                        C(SizeOf(f.Kind) > 0, w, "v" + v.Number + " field " + f.Path + ": unknown kind '" + f.Kind + "'");
                        sum += Math.Max(0, SizeOf(f.Kind));
                        if (f.Version == v.Number)
                            C(seen.Add(f.Path), w, "field " + f.Path + " listed twice");
                    }
                    C(sum == v.Bytes, w, "v" + v.Number + " says " + v.Bytes + " bytes, its fields add up to " + sum);
                    C(v.Bytes > prevBytes, w, "v" + v.Number + " (" + v.Bytes + " B) does not grow past v" + (v.Number - 1) + " (" + prevBytes + " B): append only");
                    prevBytes = v.Bytes;
                    rendered.Add(Render(row, v));
                }
            }
            foreach (string s in Shipped)
                C(rendered.Contains(s), "SL-T shipped", "a SHIPPED entry is no longer in SaveLayouts.All verbatim (edited or removed): " + s);
            foreach (string r in rendered)
                if (Array.IndexOf(Shipped, r) < 0)
                    Program.Info("SL-T unreleased layout entry (add it to Shipped when it ships): " + r);
        }

        // SL-L

        private sealed class Codec
        {
            public Action<object, ByteWriter> Write;
            public Func<object, ByteReader, object> Read;
        }

        private static Codec CodecOf(Type t)
        {
            if (typeof(ISerializable).IsAssignableFrom(t))
                return new Codec
                {
                    Write = (box, w) => ((ISerializable)box).Serialize(w),
                    Read = (box, r) => { ((ISerializable)box).Deserialize(r); return box; },
                };
            if (t == typeof(SkiResortHistoryMonth))   // ECS half (SkiResortHistorySystem.cs) forwards to WriteTo / ReadFrom
                return new Codec
                {
                    Write = (box, w) => { var m = (SkiResortHistoryMonth)box; var ww = w; m.WriteTo(ref ww); },
                    Read = (box, r) => { var m = (SkiResortHistoryMonth)box; var rr = r; m.ReadFrom(ref rr); return m; },
                };
            if (t == typeof(SkiResortMonthInProgress))
                return new Codec
                {
                    Write = (box, w) => { var m = (SkiResortMonthInProgress)box; var ww = w; m.WriteTo(ref ww); },
                    Read = (box, r) => { var m = (SkiResortMonthInProgress)box; var rr = r; m.ReadFrom(ref rr); return m; },
                };
            return null;
        }

        /// <summary>Every persisted-candidate leaf field of the struct, nested value types flattened as "outer.inner".</summary>
        private static void Flatten(Type t, string prefix, List<FieldInfo> chain, Dictionary<string, FieldInfo[]> into)
        {
            foreach (FieldInfo f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                var c = new List<FieldInfo>(chain) { f };
                Type ft = f.FieldType;
                if (ft.IsValueType && !ft.IsPrimitive && !ft.IsEnum && ft != typeof(Entity))
                    Flatten(ft, prefix + f.Name + ".", c, into);
                else
                    into[prefix + f.Name] = c.ToArray();
            }
        }

        private static object GetPath(object box, FieldInfo[] chain)
        {
            object o = box;
            foreach (FieldInfo f in chain)
                o = f.GetValue(o);
            return o;
        }

        private static void SetPath(object box, FieldInfo[] chain, int i, object value)
        {
            if (i == chain.Length - 1) { chain[i].SetValue(box, value); return; }
            object inner = chain[i].GetValue(box);   // a boxed copy of the nested struct
            SetPath(inner, chain, i + 1, value);
            chain[i].SetValue(box, inner);
        }

        private static void OneType(SaveLayouts.Layout row)
        {
            string w = "SL-L " + row.TypeName;
            Type t = typeof(SaveLayouts).Assembly.GetType(SaveLayouts.Namespace + "." + row.TypeName);
            if (!C(t != null, w, "no such type in " + SaveLayouts.Namespace + " (renamed? the save names it by AQN)"))
                return;
            Codec codec = CodecOf(t);
            if (!C(codec != null, w, "no codec in tests/SaveBatch LayoutTests.CodecOf (a new persisted type needs one)"))
                return;

            // CurrentVersion == the table's last entry
            FieldInfo cv = t.GetField("CurrentVersion", BindingFlags.Public | BindingFlags.Static);
            byte current = cv != null ? (byte)cv.GetRawConstantValue() : (byte)0;
            C(cv != null && current == row.Current.Number, w,
              "CurrentVersion " + current + " != SaveLayouts' last entry v" + row.Current.Number + " (bump both, append an entry)");

            // struct fields == table fields (+ m_Version, never written as such)
            var leaves = new Dictionary<string, FieldInfo[]>();
            Flatten(t, "", new List<FieldInfo>(), leaves);
            List<Field> all = Fields(row, row.Current.Number);
            var tablePaths = new HashSet<string>(all.Select(f => f.Path));
            foreach (string p in leaves.Keys)
                if (p != "m_Version")
                    C(tablePaths.Contains(p), w, "field " + p + " is not in SaveLayouts (added without a version entry? a non-persisted field does not belong in a persisted struct)");
            foreach (Field f in all)
            {
                if (!C(leaves.ContainsKey(f.Path), w, "SaveLayouts names " + f.Path + ", the struct has no such field (renamed or removed: forbidden, rule 5)"))
                    continue;
                Type ft = leaves[f.Path][leaves[f.Path].Length - 1].FieldType;
                C(ft == ClrOf(f.Kind), w, f.Path + " is " + ft.Name + ", SaveLayouts says " + f.Kind + " (retype: forbidden, rule 5)");
            }
            if (!C(leaves.ContainsKey("m_Version"), w, "no m_Version field (the house pattern: the version read back)"))
                return;
            if (all.Any(f => !leaves.ContainsKey(f.Path) || leaves[f.Path][leaves[f.Path].Length - 1].FieldType != ClrOf(f.Kind)))
                return;   // reported above; the byte checks below would only repeat it

            // Serialize: exact bytes, version byte first, every field at its offset
            object src = Activator.CreateInstance(t);
            SetPath(src, leaves["m_Version"], 0, (byte)0xEE);
            for (int i = 0; i < all.Count; i++)
                SetPath(src, leaves[all[i].Path], 0, Sentinel(all[i].Kind, i));
            var bw = new ByteWriter();
            codec.Write(src, bw);
            if (!C(bw.Bytes.Count == row.Current.Bytes, w, "Serialize wrote " + bw.Bytes.Count + " bytes, SaveLayouts v" + row.Current.Number + " says " + row.Current.Bytes + " (a field added / removed without a version bump?)"))
                return;
            C(bw.Bytes[0] == current, w, "first byte " + bw.Bytes[0] + ", want CurrentVersion " + current + " (m_Version held 0xEE)");
            var walk = new ByteReader(bw.Bytes) { Pos = 1 };
            for (int i = 0; i < all.Count; i++)
            {
                int at = walk.Pos;
                object got = Decode(walk, all[i].Kind);
                object want = Sentinel(all[i].Kind, i);
                C(Equals(got, want), w, "byte " + at + " should hold " + all[i].Path + " (" + all[i].Kind + ") = " + want + ", reads " + got + " (order / kind drift)");
            }

            // round trip: exact consumption, every field back, rewrite identical
            var rd = new ByteReader(bw.Bytes);
            object back;
            try { back = codec.Read(Activator.CreateInstance(t), rd); }
            catch (Exception e) { C(false, w, "Deserialize threw: " + e.Message); return; }
            C(rd.Pos == bw.Bytes.Count, w, "Deserialize read " + rd.Pos + " of " + bw.Bytes.Count + " bytes (the game throws 'Data size mismatch', ComponentDataSerializer.cs:139)");
            C(Equals(GetPath(back, leaves["m_Version"]), current), w, "m_Version after a load = " + GetPath(back, leaves["m_Version"]) + ", want " + current);
            for (int i = 0; i < all.Count; i++)
                C(Equals(GetPath(back, leaves[all[i].Path]), Sentinel(all[i].Kind, i)), w, all[i].Path + " did not come back");
            var bw2 = new ByteWriter();
            codec.Write(back, bw2);
            C(bw2.Bytes.SequenceEqual(bw.Bytes), w, "read-then-write is not byte-identical");

            // every older version still loads whole (rule 5)
            foreach (SaveLayouts.LayoutVersion v in row.Versions)
            {
                if (v.Number == row.Current.Number)
                    continue;
                List<Field> old = Fields(row, v.Number);
                var ob = new ByteWriter();
                ob.Write(v.Number);
                for (int i = 0; i < old.Count; i++)
                    Encode(ob, old[i].Kind, Sentinel(old[i].Kind, i));
                string wv = w + " v" + v.Number + " blob";
                if (!C(ob.Bytes.Count == v.Bytes, wv, "built " + ob.Bytes.Count + " bytes, table says " + v.Bytes))
                    continue;
                var ord = new ByteReader(ob.Bytes);
                object ov;
                try { ov = codec.Read(Activator.CreateInstance(t), ord); }
                catch (Exception e) { C(false, wv, "an old save's value no longer loads: " + e.Message + " (read new fields under `if (m_Version >= N)`)"); continue; }
                C(ord.Pos == v.Bytes, wv, "read " + ord.Pos + " of " + v.Bytes + " bytes (read new fields under `if (m_Version >= N)`)");
                C(Equals(GetPath(ov, leaves["m_Version"]), v.Number), wv, "m_Version = " + GetPath(ov, leaves["m_Version"]));
                for (int i = 0; i < old.Count; i++)
                    C(Equals(GetPath(ov, leaves[old[i].Path]), Sentinel(old[i].Kind, i)), wv, old[i].Path + " did not come back");
            }
            Program.Info("SL-L " + row.TypeName + " v" + row.Current.Number + " = " + row.Current.Bytes + " B (" + all.Count + " fields" +
                         (row.Versions.Length > 1 ? ", " + (row.Versions.Length - 1) + " older version(s) load" : "") + ")");
        }
    }
}
