// The frozen table of every persisted type of this mod, APPEND ONLY: one row per ISerializable type, one entry per layout
// version. An entry lists the fields that version appended, in write order after the leading version byte, as "field:kind"
// (u8 = 1 byte, i32 = 4, i64 = 8, f64 = 8, entity = int32 saved index; Colossal BinaryWriter.cs:128-141 / :305-354, 1.6.2f1),
// and the total bytes a value of that version takes in the save.
// tests/SaveBatch checks the serializers, the struct fields and the built DLL's [FormerlySerializedAs] self-aliases against
// this table; the newest release/CHANGELOG.md entry's "Layout versions:" line must equal ChangelogLine.
// Never edit a shipped entry, reorder a row's fields, or delete a row of a shipped type: the bytes are in players' saves.
// Pure file (System only); tests/SaveBatch links it. How to bump a layout: EngineNotes/SaveLayouts.md

namespace SkiResortMod.Systems
{
    public static class SaveLayouts
    {
        /// <summary>Namespace and assembly name that every row's saved AssemblyQualifiedName starts with; never change either
        /// (ComponentSerializer.cs:33-41).</summary>
        public const string Namespace = "SkiResortMod.Systems";
        public const string AssemblyName = "SkiResortMod";

        /// <summary>The csproj's pinned AssemblyVersion (SAVE IDENTITY comment in SkiResortMod.csproj).</summary>
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
                                   "m_RunCost:i32 m_BuildingsCost:i32 m_WagesCost:i32 m_Attraction:i32 m_SnowPct:u8"),
                new LayoutVersion(2, 63, "m_SnowDepthCm:i32")),
            new Layout("SkiResortMonthInProgress", "MonthInProgress", KindComponent,
                new LayoutVersion(1, 142, "m_Flags:u8 m_MonthKey:i32 m_FramesSimulated:i32 " +
                                    "m_Month.TouristGuests:i32 m_Month.TouristParties:i32 m_Month.LocalGuests:i32 " +
                                    "m_Month.TouristOnLeisure:i32 m_Month.TicketsPaid:i32 m_Month.TicketsUnpaid:i32 " +
                                    "m_Month.TicketsFree:i32 m_Month.TicketIncome:i64 m_Month.LiftCharged:f64 " +
                                    "m_Month.LodgeSpends:i32 m_Month.LodgeSpendIncome:i64 m_Month.PassesSold:i32 " +
                                    "m_Month.PassIncome:i64 m_Month.PassHolderGuests:i32 m_Month.PassesPre:i32 " +
                                    "m_Month.PassIncomePre:i64 m_Month.Lessons:i32 m_Month.LessonIncome:i64 m_Month.RunUpkeep:i64 " +
                                    "m_PassSeasonKey:i32 m_PassSeasonSold:i32 m_PassSeasonIncome:i64 m_PassSeasonPre:i32 " +
                                    "m_PassSeasonPreIncome:i64"),
                new LayoutVersion(2, 150, "m_Month.BusCharged:f64")),
            new Layout("SkiRunPropMember", "RunPropMember", KindComponent,
                new LayoutVersion(1, 6, "m_Run:entity m_Class:u8")),
            new Layout("SkiRunPropSettings", "RunPropSettings", KindComponent,
                new LayoutVersion(1, 13, "m_GunSpacingM:i32 m_LightSpacingM:i32 m_AppliedKey:i32")),
            new Layout("SkiLodgeNumber", "LodgeNumber", KindComponent,
                new LayoutVersion(1, 9, "m_Sign:entity m_Number:i32")),
            new Layout("SkiTrackSnow", "TrackSnow", KindComponent,
                new LayoutVersion(1, 5, "m_MadePermille:i32")),
            new Layout("SkiRunName", "RunName", KindComponent,
                new LayoutVersion(1, 6, "m_Index:i32 m_Form:u8")),
        };
        // FROZEN-END

        /// <summary>The [FormerlySerializedAs] self-alias of a row: the saved AQN without Version / Culture / PublicKeyToken, the key
        /// Colossal's lookup reaches by stripping from the right (ComponentSerializer.cs:51-65, UpdateTypeTable :113-121).</summary>
        public static string Alias(Layout row) => Namespace + "." + row.TypeName + ", " + AssemblyName;

        /// <summary>The fixed "Layout versions:" changelog line, built from the rows in order.</summary>
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
