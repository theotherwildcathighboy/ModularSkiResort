// Persisted area of a resident household's season-pass standing (T284, 1.0.4), beside SkiSeasonPass (whose v1 layout is frozen): the ski
// area whose sale decided the household this season, and whether that area sold pool passes (its plan was the Options default). The rule is
// pure: SkiPassPlanRules.PassValidAt. Absent (a 1.0.3 standing) = good at every area, as 1.0.3. A pool pass is good at every area on the
// default plan that season; any other pass only at its own area (a bulldozed sign loads Null: good nowhere, no refund).
// A NEW type with a new SaveLayouts row. An older build skips it cleanly; its standings then count at every area again.
// SkiLiftTicketSystem alone writes it, on the main thread, together with SkiSeasonPass (ApplySale, ApplyPassWrites). The jobs read it.
// Layout v1 (6 bytes): m_Flags u8 (SkiPassPlanRules.AreaFlagPool) | m_Sign entity. This layout never changes; a new field is a new type.
// Never rename the type or move its namespace without [FormerlySerializedAs].

using Colossal.Serialization.Entities;
using Unity.Entities;

namespace SkiResortMod.Systems
{
    // Stripped saved AQN as a literal. Rules: SkiRunBinding.cs "SELF-ALIAS"
    [FormerlySerializedAs("SkiResortMod.Systems.SkiSeasonPassArea, SkiResortMod")]
    public struct SkiSeasonPassArea : IComponentData, IQueryTypeParameter, ISerializable
    {
        /// <summary>Layout version written by this build.</summary>
        public const byte CurrentVersion = 1;

        /// <summary>SkiPassPlanRules.AreaFlagPool or 0.</summary>
        public byte m_Flags;

        /// <summary>The Welcome Sign whose sale decided the household. Null after a load means the sign was gone at save time.</summary>
        public Entity m_Sign;

        /// <summary>Layout version this value was created with or loaded from (not written: Serialize writes CurrentVersion).</summary>
        public byte m_Version;

        public SkiSeasonPassArea(Entity sign, byte flags)
        {
            m_Flags = flags;
            m_Sign = sign;
            m_Version = CurrentVersion;
        }

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            // Typed locals pin the IWriter.Write(byte) overload
            byte version = CurrentVersion;
            writer.Write(version);
            byte flags = m_Flags;
            writer.Write(flags);
            writer.Write(m_Sign);     // remapped on load (Owner.cs:17)
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            reader.Read(out m_Version);
            reader.Read(out m_Flags);
            reader.Read(out m_Sign);  // -1 / out of range -> Entity.Null (Owner.cs:22)
        }
    }
}
