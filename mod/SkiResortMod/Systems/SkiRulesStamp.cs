// Serializable per-object rules stamp (T229, 1.0.2, DECISIONS 670): the rules level (SkiRulesLevel.Current) an object was built under.
// On lift line sections (edges), our catalog buildings, runs and cat tracks (SkiRulesStampSystem's stamp-new pass on Created; a section
// changed by a tool that is not ours gets FlagHandEdited, on an unstamped section as a level-0 stamp), and on merged buildings (SkiMergeSystem; removed again on unmerge). The run
// re-emit carries it over (SkiRunReemitSystem). Absent = level 0 (SkiRulesLevel.LevelOf). A save never re-stamps: only creation writes it.
// A NEW type with a new SaveLayouts row (production mode: never a longer layout of a shipped type). A 1.0.0 / 1.0.1 build skips this
// unknown type cleanly (ComponentSerializer.cs:51-73, the SkiTrackPack precedent of 1.0.1), and objects loaded there lose the stamp on
// re-save, reading 0 again: safe, because a level is a hint and every sync step checks the real state.
// Layout v1 (6 bytes): m_Rules (i32), m_Flags (u8: SkiRulesLevel.FlagKept from the Sync card's "Keep as is", FlagHandEdited; other bits reserved). This layout never
// changes; a new field is a new type. Never rename the type or move its namespace without [FormerlySerializedAs].

using Colossal.Serialization.Entities;
using Unity.Entities;

namespace SkiResortMod.Systems
{
    // Stripped saved AQN as a literal. Rules: SkiRunBinding.cs "SELF-ALIAS"
    [FormerlySerializedAs("SkiResortMod.Systems.SkiRulesStamp, SkiResortMod")]
    public struct SkiRulesStamp : IComponentData, IQueryTypeParameter, ISerializable
    {
        /// <summary>Layout version written by this build.</summary>
        public const byte CurrentVersion = 1;

        /// <summary>The rules level this object satisfies (SkiRulesLevel.Current when built; Unknown on a re-emit of an unstamped run).</summary>
        public int m_Rules;

        /// <summary>SkiRulesLevel.Flag* bits. 0 in 1.0.2.</summary>
        public byte m_Flags;

        /// <summary>Layout version this value was created with or loaded from (not written: Serialize writes CurrentVersion).</summary>
        public byte m_Version;

        public SkiRulesStamp(int rules)
        {
            m_Rules = rules;
            m_Flags = 0;
            m_Version = CurrentVersion;
        }

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            // Typed locals pin the IWriter.Write(byte) overload
            byte version = CurrentVersion;
            writer.Write(version);
            writer.Write(m_Rules);
            byte flags = m_Flags;
            writer.Write(flags);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            reader.Read(out m_Version);
            reader.Read(out m_Rules);
            reader.Read(out m_Flags);
        }
    }
}
