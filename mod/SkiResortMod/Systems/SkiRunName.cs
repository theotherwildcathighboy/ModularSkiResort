// Serializable trail name pick of a run (R3, DECISIONS 578). It holds an index into the trail-name list (SkiRunNameRules, locale keys
// SkiResort.RUN_NAME:<index>). The index is -1 when every name was taken (the catalog then shows "Run N" / "Cat track N"). It also holds the form
// the name takes ("Upper ..." / "Lower ...", SkiRunNameRules.Form*). A persisted component: its type name and Serialize layout
// are a save-format contract (see SaveLayouts). SkiRunNameSystem adds it once the run is rated. SkiRunReemitSystem copies it to the
// re-emitted run. It lives on the run, so a bulldozed run frees its name with it. Layout v1 (6 bytes). After the 1.0 freeze it never
// changes (no payload length is written). New fields go in a new type. Never rename the type or move its namespace without [FormerlySerializedAs].

using Colossal.Serialization.Entities;
using Unity.Entities;

namespace SkiResortMod.Systems
{
    // Stripped saved AQN as a literal. Rules in SkiRunBinding.cs "SELF-ALIAS"
    [FormerlySerializedAs("SkiResortMod.Systems.SkiRunName, SkiResortMod")]
    public struct SkiRunName : IComponentData, IQueryTypeParameter, ISerializable
    {
        /// <summary>Layout version written by this build.</summary>
        public const byte CurrentVersion = 1;

        /// <summary>0 .. SkiRunNameRules.Count - 1, or SkiRunNameRules.None (show "Run N").</summary>
        public int m_Index;

        /// <summary>SkiRunNameRules.FormNone, FormUpper or FormLower.</summary>
        public byte m_Form;

        /// <summary>Layout version this pick was created with or loaded from (not written: Serialize writes CurrentVersion).</summary>
        public byte m_Version;

        public SkiRunName(int index, byte form)
        {
            m_Index = index;
            m_Form = form;
            m_Version = CurrentVersion;
        }

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            // Typed locals pin the IWriter.Write(byte) overload
            byte version = CurrentVersion;
            writer.Write(version);
            writer.Write(m_Index);
            byte form = m_Form;
            writer.Write(form);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            reader.Read(out m_Version);
            reader.Read(out m_Index);
            reader.Read(out m_Form);
        }
    }
}
