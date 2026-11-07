// The persisted run→sign link. Save-format contract: this type's name, namespace and Serialize layout must never change.
// Layout v1 is frozen: byte version | Entity gate (int32 index) | byte flags | int32 paid = 10 bytes per run, little-endian.
// New fields go in a new ISerializable companion component. m_Gate == Null after a load means the sign was gone at save time.
// Run areas carry no Owner(sign). The bulldozer follows an owned area's Owner up to the sign (Game.Areas/RaycastJobs.cs:80-93).
// So one click on a run would delete the sign and every run. The sign→runs cascade is SkiRunGateCascadeSystem, keyed on m_Gate.
// Flags:
// - FlagPaid = the one-time bind fee is settled (never charge again).
// - FlagRestored = linked by the load pass without a charge, m_PaidAmount then 0.
// m_PaidAmount keeps what the run paid, so a refund needs no save-format change.
// Engine notes: EngineNotes/SkiRunBinding.md

using Colossal.Serialization.Entities;
using Unity.Entities;

namespace SkiResortMod.Systems
{
    // Self-alias: a load first tries Type.GetType(saved AQN). Only when that fails does it strip ", ..." segments from the right until
    // a key of the [FormerlySerializedAs] table hits (Colossal.Core 1.6.2f1 ComponentSerializer.cs:51-65).
    // ComponentSerializerLibrary.UpdateTypeTable :113-121 fills the table. The stripped AQN below therefore still finds this type if the version /
    // load-context part of the saved name stops resolving. A literal on purpose: an alias is an old name and must not follow a rename.
    // It must equal SaveLayouts.Alias (tests/SaveBatch SL-A reads the built DLL). Keys must be unique across the mod (UpdateTypeTable
    // uses Dictionary.Add, a duplicate throws).
    [FormerlySerializedAs("SkiResortMod.Systems.SkiRunBinding, SkiResortMod")]
    public struct SkiRunBinding : IComponentData, IQueryTypeParameter, ISerializable
    {
        /// <summary>Layout version written by this build (see engine notes). v1 is frozen. Before 1.0 a bump appends a field and Deserialize
        /// branches on it. After 1.0 the layout never changes (a new type carries new fields).</summary>
        public const byte CurrentVersion = 1;

        /// <summary>The one-time bind fee is settled for this run: never charge it again.</summary>
        public const byte FlagPaid = 1;

        /// <summary>Linked by the load pass without a charge (lost link).</summary>
        public const byte FlagRestored = 2;

        /// <summary>The paid-once memory is set (DECISIONS 577): the bits SkiRunFees.PaidGroomed / PaidSnowmaking / PaidLights (4, 8, 16) are
        /// the feature steps this run paid. A paid run without it was bound before the memory.
        /// SkiRunOwnershipSystem.CreditKnownSteps credits it with its features at the next load.</summary>
        public const byte FlagStepsKnown = 32;

        /// <summary>The Ski Area Welcome Sign this run belongs to. Entity.Null = a paid orphan
        /// (its sign died without our cascade, or was gone at save time). It is unbound for snow,
        /// bills and score, and re-links free to the nearest sign within the tether.</summary>
        public Entity m_Gate;

        /// <summary>FlagPaid | FlagRestored | FlagStepsKnown | the paid steps (SkiRunFees.PaidSteps).</summary>
        public byte m_Flags;

        /// <summary>What this run paid in one-time fees: at bind, plus every feature step paid later (SettleFeatureSteps). 0 when restored
        /// (FlagRestored). A charge skipped because the City has no PlayerMoney (warned once per load) still records the cost.</summary>
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
            // v1 layout (engine notes (a)). Version first, always CurrentVersion: it describes the bytes written now, whatever version the value
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
            // v1 layout (frozen, see engine notes).
            reader.Read(out m_Gate);     // Owner.cs:22 shape, -1 / out of range -> Entity.Null
            reader.Read(out m_Flags);
            reader.Read(out m_PaidAmount);
        }
    }
}
