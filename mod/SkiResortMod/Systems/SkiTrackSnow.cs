// Serializable made snow of a cat track: the share of its vertical its guns have covered (RunSnowRules.TrackMadeSnow), so a load keeps
// a track open where its made snow was, as the saved snow map keeps a run's. A persisted component: its type name and Serialize layout
// are a save-format contract (see SaveLayouts). Added and written by SkiSnowInjectionSystem (RefreshRunSnow) on bound, measured or
// estimated cat tracks only; read once per track after a load. Layout v1 (5 bytes): after the 1.0 freeze it never changes (no payload
// length is written): new fields go in a new type. Never rename the type or move its namespace without [FormerlySerializedAs].

using Colossal.Serialization.Entities;
using Unity.Entities;

namespace SkiResortMod.Systems
{
    // Stripped saved AQN as a literal; rules in SkiRunBinding.cs "SELF-ALIAS"
    [FormerlySerializedAs("SkiResortMod.Systems.SkiTrackSnow, SkiResortMod")]
    public struct SkiTrackSnow : IComponentData, IQueryTypeParameter, ISerializable
    {
        /// <summary>Layout version written by this build.</summary>
        public const byte CurrentVersion = 1;

        /// <summary>Made share in thousandths (0..1000).</summary>
        public int m_MadePermille;

        /// <summary>Layout version this value was created with or loaded from (not written: Serialize writes CurrentVersion).</summary>
        public byte m_Version;

        public SkiTrackSnow(int madePermille)
        {
            m_MadePermille = madePermille;
            m_Version = CurrentVersion;
        }

        /// <summary>The share as 0..1 (a value out of range from a hand-edited save is clamped).</summary>
        public float Share => m_MadePermille <= 0 ? 0f : m_MadePermille >= 1000 ? 1f : m_MadePermille / 1000f;

        public static int ToPermille(float share) => !(share > 0f) ? 0 : share >= 1f ? 1000 : (int)System.Math.Round(share * 1000f);

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            // Typed locals pin the IWriter.Write(byte) overload
            byte version = CurrentVersion;
            writer.Write(version);
            writer.Write(m_MadePermille);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            reader.Read(out m_Version);
            reader.Read(out m_MadePermille);
        }
    }
}
