// The persisted run→sign link. Save-format contract: this type's name, namespace and Serialize layout must never change.
//
// Run areas carry no Owner(sign) because in game mode the bulldozer walks an owned area's Owner up to the sign
// (Game.Areas/RaycastJobs.cs:80-93), so clicking one run deleted the sign and, through its SubArea buffer, every run; a border
// prop is a second path to the same result (Game.Objects/RaycastJobs.cs:328-442). Without Owner both walks stop at the run.
// The sign→runs cascade is SkiRunGateCascadeSystem, keyed on m_Gate.
//
// Serialization (checked against the installed 1.6.2f1 Colossal.Core.dll, which is not in tools/decompile: member names, not file:line):
//   * Shape copies Game.Common/Owner.cs:15-23 (ISerializable; writer.Write(Entity) / reader.Read(out Entity)) with the version
//     byte first, like SkiRunTreeMember. Mod component types reach the save serializer because ComponentSerializerLibrary.Initialize
//     adds every ISerializable component from an assembly other than Game.dll to the set SerializerSystem's query is built from
//     (SerializerSystem.cs:194-209).
//   * Entity remap: BinaryWriter.Write(Entity) writes the saved index only when the live entity is in the save's entity table with
//     a matching Version, else -1; BinaryReader.Read(out Entity) maps -1 or out of range to Entity.Null. The table excludes Temp and
//     Deleted (SerializerSystem.cs:198-208). So m_Gate == Null after a load means the sign was gone when the city was saved
//     (a "paid orphan"), never corruption.
//   * Framing: component data is one size-framed block per (archetype, component type); DeserializeComponentDataJob throws
//     "Data size mismatch when deserializing component" when the bytes read differ from the block size. A build that does not know
//     the type (ComponentSerializer.DeserializeType: Type.GetType fails and no [FormerlySerializedAs] matches) logs
//     "Not serializable type: {0}" to Unity Player.log, not the mod log, and skips the block with ObsoleteComponentSerializer.
//   * Consequences (release notes):
//     (a) Layout v1 is frozen: byte version | Entity gate (int32 index) | byte flags | int32 paid = 10 bytes per run, little-endian.
//         After 1.0 it never changes: no payload length is written, so an older build reading a longer element reads the rest of
//         the block shifted. New fields go in a new ISerializable type (companion component), which an older build skips as an
//         unknown type. Before the 1.0 freeze an append + CurrentVersion bump (branch on m_Version in Deserialize) is still allowed.
//     (b) Downgrade hazard: a build without this type loads the save without error (block skipped), then its sweep re-binds and
//         re-charges every run (4,000-20,000 each) and re-adds Owner + the sign's SubArea buffer (the one-run-bulldoze trap).
//         Never open a save made by this build in an older one. A v1 build reading a future v2 block throws "Data size mismatch"
//         (game outcome unverified).
//     (c) Uninstall leaves plain run surfaces with no Owner and no binding; reinstalling re-links them free at the next load.
//   * The saved type name is GetComponentType().AssemblyQualifiedName (ComponentSerializer.SerializeType). Never rename this type,
//     move its namespace or rename the SkiResortMod assembly without [FormerlySerializedAs]: the type table resolves old names only
//     through that attribute (ComponentSerializerLibrary.UpdateTypeTable). Whether Mono's Type.GetType tolerates a changed
//     AssemblyVersion of this non-strong-named assembly is unverified; the csproj pins AssemblyVersion 1.0.0.0 and the type carries
//     a self-alias (below) as a fallback (ComponentSerializer.cs:51-65; inferred from the decompile, not seen in game).
//
// Flags: FlagPaid = the one-time bind fee is settled for this run: never charge it again, also not after the sign dies and the run
// re-links. FlagRestored = linked by the load pass without a charge (lost link); m_PaidAmount is then 0. m_PaidAmount keeps what
// the run paid so a refund can be added without a save-format change.

using Colossal.Serialization.Entities;
using Unity.Entities;

namespace SkiResortMod.Systems
{
    // Self-alias: a load first tries Type.GetType(saved AQN); only when that fails does it strip ", ..." segments from the right until
    // a key of the [FormerlySerializedAs] table hits (Colossal.Core 1.6.2f1 ComponentSerializer.cs:51-65; the table is filled by
    // ComponentSerializerLibrary.UpdateTypeTable :113-121). The stripped AQN below therefore still finds this type if the version /
    // load-context part of the saved name stops resolving. A literal on purpose: an alias is an old name and must not follow a rename.
    // Must equal SaveLayouts.Alias (tests/SaveBatch SL-A reads the built DLL); keys must be unique across the mod (UpdateTypeTable
    // uses Dictionary.Add, a duplicate throws).
    [FormerlySerializedAs("SkiResortMod.Systems.SkiRunBinding, SkiResortMod")]
    public struct SkiRunBinding : IComponentData, IQueryTypeParameter, ISerializable
    {
        /// <summary>Layout version written by this build (see header). v1 is frozen; before 1.0 a bump appends a field and branches on it
        /// in Deserialize; after 1.0 the layout never changes (a new type carries new fields).</summary>
        public const byte CurrentVersion = 1;

        /// <summary>The one-time bind fee is settled for this run: never charge it again.</summary>
        public const byte FlagPaid = 1;

        /// <summary>Linked by the load pass without a charge (lost link).</summary>
        public const byte FlagRestored = 2;

        /// <summary>The Ski Area Welcome Sign this run belongs to. Entity.Null = a paid orphan
        /// (its sign died without our cascade, or was gone at save time): unbound for snow,
        /// bills and score, and re-links free to the nearest sign within the tether.</summary>
        public Entity m_Gate;

        /// <summary>FlagPaid | FlagRestored.</summary>
        public byte m_Flags;

        /// <summary>What this run paid at bind time; 0 when restored (FlagRestored). A charge skipped because the City has no
        /// PlayerMoney (warned once per load) still records the cost.</summary>
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
            // v1 layout (header (a)). Version first, always CurrentVersion: it describes the bytes written now, whatever version the value
            // was loaded from. A local typed byte pins the IWriter.Write(byte) overload (SkiRunTreeMember precedent).
            byte version = CurrentVersion;
            writer.Write(version);
            writer.Write(m_Gate);        // Entity reference, remapped on load (Owner.cs:17)
            writer.Write(m_Flags);       // byte
            writer.Write(m_PaidAmount);  // int32
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            reader.Read(out m_Version);
            // v1 layout (frozen; see header).
            reader.Read(out m_Gate);     // Owner.cs:22 shape; -1 / out of range -> Entity.Null
            reader.Read(out m_Flags);
            reader.Read(out m_PaidAmount);
        }
    }
}
