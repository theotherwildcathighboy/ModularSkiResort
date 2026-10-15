// SkiSeasonPass — the persisted season-pass standing of a RESIDENT household (DECISIONS 240, 241;
// docs/research/econ-2/build/season-pass-v1.md). Our persisted-component house pattern is
// SkiRunBinding.cs; its header holds the verified serialization facts and they apply here unchanged:
//   * Mod components are registered with the save serializer automatically (ComponentSerializerLibrary walks every
//     TypeManager ISerializable type of an assembly other than Game.dll, Game.Serialization/SerializerSystem.cs:194-209);
//     nothing is added in Mod.cs.
//   * LAYOUT v1 IS FROZEN: byte version | int32 SeasonKey | int32 PricePaid | byte Flags = 10 bytes per household,
//     little-endian. A v2 may only APPEND fields and must branch on m_Version in Deserialize.
//   * NEVER rename this type, move its namespace or rename the assembly (the saved type name is its
//     AssemblyQualifiedName); a build that does not know the type skips the block ("Not serializable type" in Player.log).
//
// MEANING. On the household entity (a resident household; tourists never get one). SeasonKey = the ski-season year the
// standing is for (SkiMoneyRules.SeasonKeyOf: July on = the year, else the year before). A component of another season
// counts as absent (SkiMoneyRules.PassStandingOf) and is overwritten the next time the household is decided.
//   Flags 0                         → the household HOLDS a pass for SeasonKey: PricePaid is what it paid (locked at the sale).
//   Flags & SkiMoneyRules.PassFlagDeclined → it was asked this season and did not buy (rolled out, or could not pay): it pays
//                                    day tickets all season and is not asked again; PricePaid is 0.
// WHO WRITES: SkiLiftTicketSystem only, on the main thread in the tick after the job decided (never in a Burst job: a
// structural change). WHO READS: the ticket job and the pre-season sale job (ComponentLookup, read-only) and the ski-day
// decision job (a holder gets P = 1).
//
// Created 2026-10-14.

using Colossal.Serialization.Entities;
using Unity.Entities;

namespace SkiResortMod.Systems
{
    public struct SkiSeasonPass : IComponentData, IQueryTypeParameter, ISerializable
    {
        /// <summary>Layout version written by this build (see header). v1 is frozen; bump only when APPENDING a field.</summary>
        public const byte CurrentVersion = 1;

        /// <summary>The ski-season year this standing is for (SkiMoneyRules.SeasonKeyOf).</summary>
        public int m_SeasonKey;

        /// <summary>What the household paid for the pass (locked at the sale); 0 when Declined.</summary>
        public int m_PricePaid;

        /// <summary>SkiMoneyRules.PassFlagDeclined or 0 (a holder).</summary>
        public byte m_Flags;

        /// <summary>Layout version this value was created with or loaded from. NOT written: Serialize always writes CurrentVersion.</summary>
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
            // v1 layout, byte for byte (header). Version FIRST, always the CURRENT layout's version; a local typed byte pins the
            // IWriter.Write(byte) overload (the SkiRunBinding / SkiRunTreeMember precedent).
            byte version = CurrentVersion;
            writer.Write(version);
            writer.Write(m_SeasonKey);   // int32
            writer.Write(m_PricePaid);   // int32
            writer.Write(m_Flags);       // byte
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            reader.Read(out m_Version);
            // v1 layout. A future v2 appends after m_Flags: read it under `if (m_Version >= 2)` so v1 saves keep loading.
            reader.Read(out m_SeasonKey);
            reader.Read(out m_PricePaid);
            reader.Read(out m_Flags);
        }
    }
}
