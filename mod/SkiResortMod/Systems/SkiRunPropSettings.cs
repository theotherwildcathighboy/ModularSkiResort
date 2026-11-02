// A run's two border-prop spacings, set per run from the hub's Runs view. A persisted component: its type name and Serialize layout are a
// save-format contract (see SaveLayouts). Look only: snow and upkeep never read it. Layout v1 (13 bytes): after the 1.0 freeze it never
// changes: new fields go in a new type. Never rename the type or move its namespace without [FormerlySerializedAs].
// Engine notes: EngineNotes/SkiRunPropMember.md

using Colossal.Serialization.Entities;
using Unity.Entities;

namespace SkiResortMod.Systems
{
    // Stripped saved AQN as a literal; rules in SkiRunBinding.cs "SELF-ALIAS"
    [FormerlySerializedAs("SkiResortMod.Systems.SkiRunPropSettings, SkiResortMod")]
    public struct SkiRunPropSettings : IComponentData, IQueryTypeParameter, ISerializable
    {
        /// <summary>Layout version written by this build.</summary>
        public const byte CurrentVersion = 1;

        /// <summary>Meters between guns and hydrants along the border; RunPropPlacerRules.SpacingOff = none.</summary>
        public int m_GunSpacingM;

        /// <summary>Meters between floodlight masts; RunPropPlacerRules.SpacingOff = none. On a cat track above Off = the track is lit.</summary>
        public int m_LightSpacingM;

        /// <summary>RunPropPlacerRules.RingKey of the plan that stands (0 = none yet). A load keeps a run whose key still matches, so a prop
        /// the player bulldozed stays gone until the outline or a slider changes.</summary>
        public int m_AppliedKey;

        /// <summary>Layout version this value was created with or loaded from (not written: Serialize writes CurrentVersion).</summary>
        public byte m_Version;

        public SkiRunPropSettings(int gunSpacingM, int lightSpacingM)
        {
            m_GunSpacingM = gunSpacingM;
            m_LightSpacingM = lightSpacingM;
            m_AppliedKey = 0;
            m_Version = CurrentVersion;
        }

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            // Typed locals pin the IWriter.Write overloads
            byte version = CurrentVersion;
            writer.Write(version);
            writer.Write(m_GunSpacingM);
            writer.Write(m_LightSpacingM);
            writer.Write(m_AppliedKey);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            reader.Read(out m_Version);
            reader.Read(out m_GunSpacingM);
            reader.Read(out m_LightSpacingM);
            reader.Read(out m_AppliedKey);
        }
    }
}
