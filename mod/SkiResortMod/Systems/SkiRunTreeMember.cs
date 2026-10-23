// Serializable member tag that binds a glade-tool tree to its ski run. A persisted component: its type name and Serialize
// layout are a save-format contract (see SaveLayouts).
//
// Why a tag and not Owner(run) (1.6.0f1 decompile): an object with Owner(area) is reconciled by Game.Objects.SubObjectSystem on
// ANY Updated of the area (SubObjectSystem.cs:3936-3944, area branch :936 / :1041-1048 -> RelocateSubObjects :1360-1533): every
// owned object outside the polygon, touching an edge or overlapping a kept object is moved to a random interior spot or
// deleted (:1507-1517, :1227-1268), so a tree-line belt outside the run is impossible and dense glades are scrambled by every
// polygon edit. A click on an Owner(run) tree walks the owner chain to the gate (RaycastJobs.cs:328-403), owned trees get no
// Game.Objects.Surface (GenerateObjectsSystem.cs:1264-1274), and groomed runs carry no runtime SubObject buffer
// (SubObjectReferencesSystem.cs:72 indexes it unguarded). So planted trees are free-standing and carry this tag, added to the
// Temp tree by SkiGladeTagSystem just before ApplyObjectsSystem, which keeps the same entity on create
// (ApplyObjectsSystem.cs:670-687). The run delete cascade is SkiGladeCleanupSystem's job.
//
// m_Variant (0..7) and m_PlanGen (SkiGladeCompose.PlanGeneration when planted) tell a run's members apart by the plan that
// placed them: the same (variant, generation) is matched against the plan, anything else (another variant, an older
// generation, a v1 tag) is replaced by the next apply of that layer.
//
// Serialization has the shape of Game.Common/Owner.cs:15-23: writer.Write(Entity) is the vanilla Entity-reference path, so
// m_Run is remapped on load like Owner.m_Owner. Mod component types reach the save serializer because ModManager registers
// every mod assembly with TypeManager.InitializeAdditionalTypes and marks SerializerSystem dirty (ModManager.cs:146-150).
//   Layouts (little-endian; Write(Entity) = int32 saved index or -1):
//     v1:            byte version | Entity run | byte layer                                = 6 bytes
//     v2 (current):  byte version | Entity run | byte layer | byte variant | byte planGen  = 8 bytes
// Serialize always writes CurrentVersion and the v2 layout. Deserialize reads variant + planGen only when version >= 2; a v1
// tag loads as m_Variant = VariantUnknown, m_PlanGen = 0. After the 1.0 freeze this layout never changes: no payload length is
// written, so an older build reading a longer element reads the rest of the block shifted; new fields go in a new
// ISerializable type, which an older build skips cleanly. The v1 -> v2 branch is the pre-release pattern (append + bump + branch).
// Legacy test: use m_PlanGen == 0 (or VariantUnknown), NOT m_Version < 2: a v1 tag re-saved by this build reads back as v2.
//
// Framing (Colossal.Core 1.6.2f1 decompile, member names only): component data is one size-framed block per (archetype,
// component type); ComponentDataSerializer.DeserializeComponentDataJob reads every entity's value between reader.Begin(out
// size) and reader.End(block), and BinaryReader.End returns false when the bytes read differ from the block size, giving
// ComponentSerializerException "Data size mismatch when deserializing component".
//   * Downgrade hazard: an older build reading a v2 block reads 6 of 8 bytes per tree and throws "Data size mismatch" (its
//     handling inside a Burst job is unverified). Never load a v2 save in an older build.
//   * Uninstall: a build that does not know the type logs Debug.LogWarningFormat("Not serializable type: {0}") to Unity
//     Player.log and skips the block; trees then load as plain vanilla trees.
//   * Never rename this type or move its namespace without [FormerlySerializedAs]: the type table resolves saved names
//     through that attribute.

using Colossal.Serialization.Entities;
using Unity.Entities;

namespace SkiResortMod.Systems
{
    // Stripped saved AQN as a literal; rules in SkiRunBinding.cs "SELF-ALIAS"
    [FormerlySerializedAs("SkiResortMod.Systems.SkiRunTreeMember, SkiResortMod")]
    public struct SkiRunTreeMember : IComponentData, IQueryTypeParameter, ISerializable
    {
        /// <summary>Layout version written by this build. v1 is frozen; after 1.0 a new type carries new fields.</summary>
        public const byte CurrentVersion = 2;

        /// <summary>m_Layer values: interior glade tree / tree-line belt tree; equal to SkiGladeCompose.LayerGlade / LayerBelt,
        /// checked at compile time in SkiGladeTagSystem.</summary>
        public const byte LayerGlade = 0;
        public const byte LayerBelt = 1;

        /// <summary>m_Variant of a v1 tag (no recorded variant).</summary>
        public const byte VariantUnknown = 0xFF;

        /// <summary>The run area this tree belongs to (no Owner). Null or a dead entity marks an orphan; SkiGladeCleanupSystem strips
        /// the tag and keeps the tree.</summary>
        public Entity m_Run;

        public byte m_Layer;

        /// <summary>v2: the plan variant 0..7 that placed this tree; VariantUnknown for a v1 tag.</summary>
        public byte m_Variant;

        /// <summary>v2: SkiGladeCompose.PlanGeneration when the tree was planted; 0 = v1 tag.</summary>
        public byte m_PlanGen;

        /// <summary>Layout version this tag was created with or loaded from (not written: Serialize writes CurrentVersion).</summary>
        public byte m_Version;

        public SkiRunTreeMember(Entity run, byte layer, byte variant, byte planGen)
        {
            m_Run = run;
            m_Layer = layer;
            m_Variant = variant;
            m_PlanGen = planGen;
            m_Version = CurrentVersion;
        }

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            // Typed locals pin the IWriter.Write(byte) overload
            byte version = CurrentVersion;
            writer.Write(version);
            writer.Write(m_Run);      // remapped on load (Owner.cs:17)
            writer.Write(m_Layer);
            writer.Write(m_Variant);
            writer.Write(m_PlanGen);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            reader.Read(out m_Version);
            reader.Read(out m_Run);   // -1 / out of range -> Entity.Null (Owner.cs:22)
            reader.Read(out m_Layer);
            if (m_Version >= 2)
            {
                reader.Read(out m_Variant);
                reader.Read(out m_PlanGen);
            }
            else
            {
                // v1 tag: no recorded plan; every compose reads it as "Other"
                m_Variant = VariantUnknown;
                m_PlanGen = 0;
            }
        }
    }
}
