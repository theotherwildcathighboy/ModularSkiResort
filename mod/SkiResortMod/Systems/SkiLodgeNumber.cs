// Serializable number of a lodge among the lodges of its tier in one ski area ("Chalet 2"). A persisted component: its type name and
// Serialize layout are a save-format contract (see SaveLayouts). LodgeNumberSystem (LodgeNumberRule) adds and replaces it. The hub's
// car space rows read the shown number. It lives on the lodge, so a bulldozed lodge frees its number with it.
// Layout v1 (9 bytes): after the 1.0 freeze it never changes (no payload length is written). New fields go in a new type.
// Never rename the type or move its namespace without [FormerlySerializedAs].
// Engine notes: EngineNotes/LodgeNumberSystem.md

using Colossal.Serialization.Entities;
using Unity.Entities;

namespace SkiResortMod.Systems
{
    // Stripped saved AQN as a literal. Rules in SkiRunBinding.cs "SELF-ALIAS"
    [FormerlySerializedAs("SkiResortMod.Systems.SkiLodgeNumber, SkiResortMod")]
    public struct SkiLodgeNumber : IComponentData, IQueryTypeParameter, ISerializable
    {
        /// <summary>Layout version written by this build.</summary>
        public const byte CurrentVersion = 1;

        /// <summary>The Welcome Sign this number was given for. Null after a load means the sign was gone at save time. A lodge linked to
        /// another sign than this one is numbered again there.</summary>
        public Entity m_Sign;

        /// <summary>1 and up, unique per (sign, lodge tier).</summary>
        public int m_Number;

        /// <summary>Layout version this number was created with or loaded from (not written: Serialize writes CurrentVersion).</summary>
        public byte m_Version;

        public SkiLodgeNumber(Entity sign, int number)
        {
            m_Sign = sign;
            m_Number = number;
            m_Version = CurrentVersion;
        }

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            // Typed locals pin the IWriter.Write(byte) overload
            byte version = CurrentVersion;
            writer.Write(version);
            writer.Write(m_Sign);     // remapped on load (Owner.cs:17)
            writer.Write(m_Number);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            reader.Read(out m_Version);
            reader.Read(out m_Sign);  // -1 / out of range -> Entity.Null (Owner.cs:22)
            reader.Read(out m_Number);
        }
    }
}
