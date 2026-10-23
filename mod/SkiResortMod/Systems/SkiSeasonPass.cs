// Persisted season-pass standing of a resident household (tourists never get one).
// Serialization follows the SkiRunBinding.cs pattern: mod ISerializable components are registered with the
// save serializer automatically (Game.Serialization/SerializerSystem.cs:194-209), nothing in Mod.cs.
//   * Layout v1 is frozen: byte version | int32 SeasonKey | int32 PricePaid | byte Flags (little-endian).
//     No payload length is written, so an older build reading a longer element would shift; new fields go in a
//     NEW ISerializable type, which older builds skip as unknown.
//   * Never rename the type, its namespace or the assembly (the saved name is the AssemblyQualifiedName).
// SeasonKey is the ski-season year (SkiMoneyRules.SeasonKeyOf); a component of another season counts as absent
// (SkiMoneyRules.PassStandingOf) and is overwritten at the next decision.
//   Flags 0: the household holds a pass; PricePaid is locked at the sale.
//   PassFlagDeclined: asked this season and did not buy; pays day tickets and is not asked again; PricePaid is 0.
// Written by SkiLiftTicketSystem only, on the main thread after the job decided (a structural change, so never
// in a Burst job). Read by the ticket job, the pre-season sale job and the ski-day decision job.

using Colossal.Serialization.Entities;
using Unity.Entities;

namespace SkiResortMod.Systems
{
    // Stripped saved AQN as a literal; rules in SkiRunBinding.cs "SELF-ALIAS"
    [FormerlySerializedAs("SkiResortMod.Systems.SkiSeasonPass, SkiResortMod")]
    public struct SkiSeasonPass : IComponentData, IQueryTypeParameter, ISerializable
    {
        /// <summary>Layout version written by this build; v1 is frozen (see header).</summary>
        public const byte CurrentVersion = 1;

        /// <summary>The ski-season year this standing is for (SkiMoneyRules.SeasonKeyOf).</summary>
        public int m_SeasonKey;

        /// <summary>What the household paid for the pass (locked at the sale); 0 when Declined.</summary>
        public int m_PricePaid;

        /// <summary>SkiMoneyRules.PassFlagDeclined or 0 (a holder).</summary>
        public byte m_Flags;

        /// <summary>Layout version loaded or created with; not written (Serialize always writes CurrentVersion).</summary>
        public byte m_Version;

        public SkiSeasonPass(int seasonKey, int pricePaid, byte flags)
        {
            m_SeasonKey = seasonKey;
            m_PricePaid = pricePaid;
            m_Flags = flags;
            m_Version = CurrentVersion;
        }

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            // Local typed byte pins the IWriter.Write(byte) overload
            byte version = CurrentVersion;
            writer.Write(version);
            writer.Write(m_SeasonKey);
            writer.Write(m_PricePaid);
            writer.Write(m_Flags);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            reader.Read(out m_Version);
            // v1 layout (frozen)
            reader.Read(out m_SeasonKey);
            reader.Read(out m_PricePaid);
            reader.Read(out m_Flags);
        }
    }
}
