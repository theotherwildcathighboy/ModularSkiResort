// SkiRunBinding — the persisted run→sign link (docs/research/run-binding-glade-spec.md §2, §3.2;
// DECISIONS 121 DEFECT 1). Our SECOND persisted component, after SkiRunTreeMember: its type
// name, namespace and Serialize layout are a save-format contract from the day it ships.
//
// WHY NOT Owner(sign) any more (spec §3.1, 1.6.0f1 decompile; verified by the spec's lenses):
//   * In game mode the bulldozer walks an owned area's Owner up to the sign (Game.Areas/
//     RaycastJobs.cs:80-93), so clicking ONE run deleted the sign and, through the sign's
//     SubArea buffer, every run (field 2026-10-01, log 1622-1623, 2038). A border prop is a
//     second path to the same result (Game.Objects/RaycastJobs.cs:328-442; 1.6.2 :334-448).
//   * Without Owner on the run, both walks stop at the run and the bulldozer deletes just
//     that run. The sign→runs cascade is ours now (SkiRunGateCascadeSystem), keyed on m_Gate.
//
// SERIALIZATION — verified 2026-10-01 against the INSTALLED 1.6.2f1 Colossal.Core.dll
// (ilspycmd 9.1 decompile into a scratch folder; Colossal.Core is not in tools/decompile, so
// these are member names, not file:line):
//   * Shape copies Game.Common/Owner.cs:15-23 (ISerializable from Colossal.Serialization.
//     Entities; writer.Write(Entity) / reader.Read(out Entity)) and the house precedent
//     SkiRunTreeMember.cs (version byte FIRST). Mod component types are registered with the
//     save serializer because ComponentSerializerLibrary.Initialize walks every TypeManager
//     type and adds each ISerializable component from an assembly other than Game.dll to the
//     serializable set that SerializerSystem's query is built from
//     (Game.Serialization/SerializerSystem.cs:194-209, identical in 1.6.2).
//   * ENTITY REMAP. BinaryWriter.Write(Entity) writes the saved index only when the live entity
//     is in the save's entity table with a matching Version, else -1; BinaryReader.Read(out
//     Entity) maps -1 or an out-of-range index to Entity.Null. The table holds SerializerSystem's
//     query, which excludes Temp and Deleted (SerializerSystem.cs:198-208). So m_Gate == Null
//     after a load means the sign was gone (or Deleted) when the city was saved — a legitimate
//     state (a "paid orphan", §3.3 row 5), NEVER corruption.
//   * FRAMING. Component data is one size-framed block per (archetype, component type):
//     EntityDeserializer.DeserializeComponents runs one serializer per type of the archetype;
//     ComponentDataSerializer.DeserializeComponentDataJob reads every entity's value inside
//     reader.Begin/End and throws ComponentSerializerException "Data size mismatch when
//     deserializing component …" when the bytes read differ from the block size. A build that
//     does NOT know the type (ComponentSerializer.DeserializeType: Type.GetType fails and no
//     [FormerlySerializedAs] matches) logs Debug.LogWarningFormat("Not serializable type: {0}")
//     — Unity Player.log, NOT the mod log — and skips the block with ObsoleteComponentSerializer
//     (no exception; the component is left off the recreated archetype).
//   * Consequences (write them into the release notes):
//       (a) LAYOUT v1 IS FROZEN: byte version | Entity gate (int32 index) | byte flags |
//           int32 paid = 10 bytes per run, little-endian (BinaryWriter.Write(uint) byte order).
//           A v2 may only APPEND fields and must branch on m_Version in Deserialize.
//       (b) DOWNGRADE HAZARD: a pre-O1 build loads a post-O1 save WITHOUT error (unknown type,
//           block skipped), then its sweep re-binds AND RE-CHARGES every run (4,000-20,000
//           each) and re-adds Owner + the sign's SubArea buffer (the one-run-bulldoze trap
//           returns). NEVER open a save made by this build in an older build. A v1 build
//           reading a future v2 block throws "Data size mismatch" (game outcome unverified).
//       (c) UNINSTALL leaves plain run surfaces with no Owner and no binding (cleaner than the
//           pre-O1 state, where Owner + SubArea kept the trap alive without the mod);
//           reinstalling re-links them free at the next load ("[restore]", D6).
//   * The saved type name is GetComponentType().AssemblyQualifiedName
//     (ComponentSerializer.SerializeType). NEVER rename this type, move its namespace or rename
//     the SkiResortMod assembly without [FormerlySerializedAs] — the type table resolves old
//     names only through that attribute (ComponentSerializerLibrary.UpdateTypeTable). Whether
//     Mono's Type.GetType tolerates a changed AssemblyVersion of this non-strong-named assembly
//     is unverified (the csproj sets none, so it stays 1.0.0.0).
//
// FLAGS. FlagPaid = the one-time bind fee is settled for this run: never charge it again, also
// not after the sign dies and the run re-links (§3.3 rows 4/5). FlagRestored = linked by the load
// pass without a charge (lost link, D6); m_PaidAmount is then 0. m_PaidAmount keeps what the run
// paid so a refund (D1, answered "no" for now) can be added without a save-format change.
//
// Created 2026-10-01 (Round O1, implementer I-1).

using Colossal.Serialization.Entities;
using Unity.Entities;

namespace SkiResortMod.Systems
{
    public struct SkiRunBinding : IComponentData, IQueryTypeParameter, ISerializable
    {
        /// <summary>Layout version written by this build (see header). v1 is frozen; bump
        /// only when APPENDING a field, and branch on it in Deserialize.</summary>
        public const byte CurrentVersion = 1;

        /// <summary>The one-time bind fee is settled for this run: never charge it again.</summary>
        public const byte FlagPaid = 1;

        /// <summary>Linked by the load pass without a charge (lost link, D6).</summary>
        public const byte FlagRestored = 2;

        /// <summary>The Ski Area Welcome Sign this run belongs to. Entity.Null = a paid orphan
        /// (its sign died without our cascade, or was gone at save time): unbound for snow,
        /// bills and score, and re-links free to the nearest sign within the tether.</summary>
        public Entity m_Gate;

        /// <summary>FlagPaid | FlagRestored.</summary>
        public byte m_Flags;

        /// <summary>What this run paid at bind time; 0 when restored (FlagRestored). A charge
        /// skipped because the City has no PlayerMoney (warned once per load) still records
        /// the cost. For a future refund (D1).</summary>
        public int m_PaidAmount;

        /// <summary>Layout version this value was created with or loaded from. NOT written:
        /// Serialize always writes CurrentVersion.</summary>
        public byte m_Version;

        public SkiRunBinding(Entity gate, byte flags, int paidAmount)
        {
            m_Gate = gate;
            m_Flags = flags;
            m_PaidAmount = paidAmount;
            m_Version = CurrentVersion;
        }

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            // v1 layout, byte for byte (header (a)). Version FIRST, always the CURRENT layout's
            // version: it describes the bytes written now, whatever version the value was
            // loaded from. A local typed byte pins the IWriter.Write(byte) overload (the
            // SkiRunTreeMember precedent).
            byte version = CurrentVersion;
            writer.Write(version);
            writer.Write(m_Gate);        // Entity reference, remapped on load (Owner.cs:17)
            writer.Write(m_Flags);       // byte
            writer.Write(m_PaidAmount);  // int32
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            reader.Read(out m_Version);
            // v1 layout. A future v2 appends after m_PaidAmount: read it under
            // `if (m_Version >= 2)` so v1 saves keep loading.
            reader.Read(out m_Gate);     // Owner.cs:22 shape; -1 / out of range -> Entity.Null
            reader.Read(out m_Flags);
            reader.Read(out m_PaidAmount);
        }
    }
}
