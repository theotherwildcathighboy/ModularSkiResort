// Serializable member tag that binds a glade-tool tree to its ski run. A persisted component: its type name and Serialize layout are a
// save-format contract (see SaveLayouts). Planted trees are free-standing and carry this tag instead of Owner(run): SubObjectSystem
// moves or deletes owned objects on every Updated of the area. Added by SkiGladeTagSystem just before ApplyObjectsSystem.
// m_Variant (0..7) and m_PlanGen tell a run's members apart by the plan that placed them; another (variant, generation) is replaced.
// Layout v2 (8 bytes) is current; after the 1.0 freeze it never changes (no payload length is written): new fields go in a new type.
// Legacy test: m_PlanGen == 0 (or VariantUnknown), NOT m_Version < 2. Never rename the type or move its namespace without [FormerlySerializedAs].
// Engine notes: EngineNotes/SkiRunTreeMember.md

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
