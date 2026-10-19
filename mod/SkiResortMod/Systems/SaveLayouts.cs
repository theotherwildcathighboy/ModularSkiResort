// SaveLayouts — THE frozen table of every persisted type of this mod (T74, DECISIONS 277 / 278; docs/research/release-update-
// constraints-2026-10-18.md §1 rows 2b / 2c / 8 / 9 / 10, §3 rules 5 / 6 / 9, §4 items 3 / 5 / 9; scaffold note docs/research/
// release-scaffolds-2026-10-19.md). One row per ISerializable type, one entry per layout version, APPEND ONLY:
//   * a version entry lists the fields that version APPENDED, in write order, after the leading version byte, as
//     "field:kind" (kind u8 = 1 byte, i32 = 4, i64 = 8, f64 = 8, entity = the int32 saved index; Colossal BinaryWriter.cs
//     :128-141 / :305-354, 1.6.2f1 Colossal.Core), and the total bytes a value of that version takes in the save.
//   * tests/SaveBatch reads this table and checks, per type: the bytes Serialize writes == the last entry's bytes; every
//     field lands at the offset the table says (order, kind); the struct's fields (reflection) == the union of the
//     entries' fields, so a field added without a new entry fails; CurrentVersion == the last entry's version; every older
//     version still deserializes (a blob of just that version's fields is read back whole). It also reads the built DLL:
//     every ISerializable type in it has a row here and a [FormerlySerializedAs] self-alias equal to Alias(row).
//   * release/CHANGELOG.md: the newest release entry's "Layout versions:" line must equal ChangelogLine (SaveBatch SL-CL).
// HOW TO BUMP (rule 5): append fields at the END of Serialize, bump the type's CurrentVersion, read the new fields under
// `if (m_Version >= N)` in Deserialize, then append ONE entry here (never edit an older one), write the new line into the
// changelog with "Do not load in older versions: yes" (rule 9). A new persisted concept is a NEW type (rule 6): add a row.
// NEVER edit a shipped entry, reorder rows' fields, or delete a row of a shipped type (the bytes are in players' saves).
//
// PURE FILE: System only (tests/SaveBatch links it). Created 2026-10-19 (T74 scaffolds).

namespace SkiResortMod.Systems
{
    public static class SaveLayouts
    {
        /// <summary>The namespace and assembly name every row's saved AssemblyQualifiedName starts with (never change either:
        /// the save stores the AQN, Colossal ComponentSerializer.cs:33-41).</summary>
        public const string Namespace = "SkiResortMod.Systems";
        public const string AssemblyName = "SkiResortMod";

        /// <summary>The csproj's pinned AssemblyVersion (SkiResortMod.csproj, SAVE IDENTITY comment); SaveBatch SL-AV reads it
        /// from the built DLL.</summary>
        public const string PinnedAssemblyVersion = "1.0.0.0";

        public const string KindComponent = "IComponentData";
        public const string KindBuffer = "IBufferElementData";

        public sealed class LayoutVersion
        {
            public readonly byte Number;
            public readonly int Bytes;
            public readonly string Appended;
            public LayoutVersion(byte number, int bytes, string appended) { Number = number; Bytes = bytes; Appended = appended; }
        }

        public sealed class Layout
        {
            public readonly string TypeName;
            public readonly string Short;
            public readonly string Kind;
            public readonly LayoutVersion[] Versions;
            public Layout(string typeName, string shortName, string kind, params LayoutVersion[] versions)
            {
                TypeName = typeName; Short = shortName; Kind = kind; Versions = versions;
            }
            public LayoutVersion Current => Versions[Versions.Length - 1];
        }

        // FROZEN-BEGIN (append only; see header)
        public static readonly Layout[] All =
        {
            new Layout("SkiRunBinding", "Binding", KindComponent,
                new LayoutVersion(1, 10, "m_Gate:entity m_Flags:u8 m_PaidAmount:i32")),
            new Layout("SkiRunTreeMember", "TreeMember", KindComponent,
                new LayoutVersion(1, 6, "m_Run:entity m_Layer:u8"),
                new LayoutVersion(2, 8, "m_Variant:u8 m_PlanGen:u8")),
            new Layout("SkiSeasonPass", "SeasonPass", KindComponent,
                new LayoutVersion(1, 10, "m_SeasonKey:i32 m_PricePaid:i32 m_Flags:u8")),
            new Layout("SkiResortHistoryMonth", "HistoryMonth", KindBuffer,
                new LayoutVersion(1, 59, "m_Flags:u8 m_MonthKey:i32 m_Tourists:i32 m_Residents:i32 m_SkiDays:i32 m_TicketsPaid:i32 " +
                                   "m_TicketIncome:i32 m_LodgeIncome:i32 m_PassIncome:i32 m_LessonIncome:i32 m_LiftCost:i32 " +
                                   "m_RunCost:i32 m_BuildingsCost:i32 m_WagesCost:i32 m_Attraction:i32 m_SnowPct:u8")),
            new Layout("SkiResortMonthInProgress", "MonthInProgress", KindComponent,
                new LayoutVersion(1, 142, "m_Flags:u8 m_MonthKey:i32 m_FramesSimulated:i32 " +
                                    "m_Month.TouristGuests:i32 m_Month.TouristParties:i32 m_Month.LocalGuests:i32 " +
                                    "m_Month.TouristOnLeisure:i32 m_Month.TicketsPaid:i32 m_Month.TicketsUnpaid:i32 " +
                                    "m_Month.TicketsFree:i32 m_Month.TicketIncome:i64 m_Month.LiftCharged:f64 " +
                                    "m_Month.LodgeSpends:i32 m_Month.LodgeSpendIncome:i64 m_Month.PassesSold:i32 " +
                                    "m_Month.PassIncome:i64 m_Month.PassHolderGuests:i32 m_Month.PassesPre:i32 " +
                                    "m_Month.PassIncomePre:i64 m_Month.Lessons:i32 m_Month.LessonIncome:i64 m_Month.RunUpkeep:i64 " +
                                    "m_PassSeasonKey:i32 m_PassSeasonSold:i32 m_PassSeasonIncome:i64 m_PassSeasonPre:i32 " +
                                    "m_PassSeasonPreIncome:i64")),
        };
        // FROZEN-END

        /// <summary>The [FormerlySerializedAs] self-alias of a row: the saved AQN with its ", Version=..", ", Culture=..",
        /// ", PublicKeyToken=.." segments stripped, i.e. the key Colossal's lookup reaches by stripping from the right
        /// (ComponentSerializer.cs:51-65; the table is filled by ComponentSerializerLibrary.UpdateTypeTable :113-121).</summary>
        public static string Alias(Layout row) => Namespace + "." + row.TypeName + ", " + AssemblyName;

        /// <summary>The changelog's fixed line (release/CHANGELOG.md, constraints §4 item 9), built from the rows in order.</summary>
        public static string ChangelogLine
        {
            get
            {
                var parts = new string[All.Length];
                for (int i = 0; i < All.Length; i++)
                    parts[i] = All[i].Short + " " + All[i].Current.Number;
                return "Layout versions: " + string.Join(", ", parts);
            }
        }
    }
}
