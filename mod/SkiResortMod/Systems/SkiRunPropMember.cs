// Serializable member tag that binds a placed gun, hydrant or mast to its ski run. A persisted component: its type name and Serialize layout
// are a save-format contract (see SaveLayouts). Placed props are free-standing and carry this tag instead of Owner(run): SecondaryObjectSystem
// reaps every Secondary child at each rebuild of the owner (SecondaryObjectSystem.cs:1159-1211) and SubObjectSystem moves or deletes every
// owned object at each Updated of the area (the SkiRunTreeMember reasoning). Added by SkiRunPropSystem.Placer.cs; plume, night light and
// prop snow read it. Layout v1 (6 bytes): after the 1.0 freeze it never changes (no payload length is written): new fields go in a new type.
// Never rename the type or move its namespace without [FormerlySerializedAs].
// Engine notes: EngineNotes/SkiRunPropMember.md

using Colossal.Serialization.Entities;
using Unity.Entities;

namespace SkiResortMod.Systems
{
    // Stripped saved AQN as a literal; rules in SkiRunBinding.cs "SELF-ALIAS"
    [FormerlySerializedAs("SkiResortMod.Systems.SkiRunPropMember, SkiResortMod")]
    public struct SkiRunPropMember : IComponentData, IQueryTypeParameter, ISerializable
    {
        /// <summary>Layout version written by this build.</summary>
        public const byte CurrentVersion = 1;

        /// <summary>The run area this prop belongs to (no Owner). Null or a dead entity marks an orphan: the placer's load sweep deletes
        /// the prop.</summary>
        public Entity m_Run;

        /// <summary>RunPropDensityRules.ClassGun, ClassHydrant or ClassMast.</summary>
        public byte m_Class;

        /// <summary>Layout version this tag was created with or loaded from (not written: Serialize writes CurrentVersion).</summary>
        public byte m_Version;

        public SkiRunPropMember(Entity run, byte propClass)
        {
            m_Run = run;
            m_Class = propClass;
            m_Version = CurrentVersion;
        }

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            // Typed locals pin the IWriter.Write(byte) overload
            byte version = CurrentVersion;
            writer.Write(version);
            writer.Write(m_Run);      // remapped on load (Owner.cs:17)
            writer.Write(m_Class);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            reader.Read(out m_Version);
            reader.Read(out m_Run);   // -1 / out of range -> Entity.Null (Owner.cs:22)
            reader.Read(out m_Class);
        }
    }
}
