// Serializable tag of a building the player merged into a ski area as a lodge (MergeRules header, research
// merge-asset-into-resort-2026-11-02.md). It lives on a top-level building the mod did not create. The building keeps its own prefab
// and vanilla behavior. LodgeIdentity resolves it to the lodge tier named here. A persisted component: its type name and Serialize
// layout are a save-format contract (see SaveLayouts). Layout v1 (11 bytes): after the release that ships it the layout never changes (no
// payload length is written). New fields go in a new type. Never rename the type or move its namespace without [FormerlySerializedAs].
// Without the mod the game drops it at load ("Not serializable type", measured in P0) and the building is plain vanilla again.
// Engine notes: EngineNotes/SkiMergeSystem.md

using Colossal.Serialization.Entities;
using Unity.Entities;

namespace SkiResortMod.Systems
{
    // Stripped saved AQN as a literal. Rules in SkiRunBinding.cs "SELF-ALIAS"
    [FormerlySerializedAs("SkiResortMod.Systems.SkiMergedLodge, SkiResortMod")]
    public struct SkiMergedLodge : IComponentData, IQueryTypeParameter, ISerializable
    {
        /// <summary>Layout version written by this build.</summary>
        public const byte CurrentVersion = 1;

        /// <summary>m_Flags bit: the AttractivenessProvider was added at merge (the prefab had none), so unmerge removes it. A provider
        /// that came with the prefab is never removed (vanilla cannot add it back: Attraction.cs:22-34).</summary>
        public const byte FlagAddedProvider = 1;

        /// <summary>m_Flags bit: the tier was lowered because the lot no longer fits it (a prefab swap). The hub says so.</summary>
        public const byte FlagTierLowered = 2;

        /// <summary>SkiCatalog.Lodges index of the tier.</summary>
        public byte m_Tier;

        public byte m_Flags;

        /// <summary>The merge fee charged so far (a tier change charges only the difference). The fee is never refunded.</summary>
        public int m_FeePaid;

        /// <summary>City-unique number given at merge. A copy that kept the component carries the same serial and is untagged.</summary>
        public int m_Serial;

        /// <summary>Layout version this tag was created with or loaded from (not written: Serialize writes CurrentVersion).</summary>
        public byte m_Version;

        public SkiMergedLodge(byte tier, byte flags, int feePaid, int serial)
        {
            m_Tier = tier;
            m_Flags = flags;
            m_FeePaid = feePaid;
            m_Serial = serial;
            m_Version = CurrentVersion;
        }

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            // Typed locals pin the IWriter.Write(byte) overload
            byte version = CurrentVersion;
            writer.Write(version);
            byte tier = m_Tier;
            writer.Write(tier);
            byte flags = m_Flags;
            writer.Write(flags);
            writer.Write(m_FeePaid);
            writer.Write(m_Serial);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            reader.Read(out m_Version);
            reader.Read(out m_Tier);
            reader.Read(out m_Flags);
            reader.Read(out m_FeePaid);
            reader.Read(out m_Serial);
        }
    }

    /// <summary>Runtime only (not ISerializable, so never saved): vanilla's own attraction value V of a merged building, read on the tick of
    /// its update slice (MergeRules.IsOwnSlice). Known = 0 until the first such tick after a merge or a load. Until then nothing is written,
    /// so the saved value stands.</summary>
    public struct SkiMergedVanillaAttraction : IComponentData, IQueryTypeParameter
    {
        public int m_Value;
        public byte m_Known;
    }
}
