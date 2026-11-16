// Serializable made-snow pack depth of a cat track (T220, 1.0.1): the depth its guns built on its made share (RunSnowRules.TrackMadeSnow
// .DepthM). SkiTrackSnow (1.0.0) saves the share only and its layout never changes, so the depth is this NEW type (production mode: a new
// persisted concept is a new type with a new SaveLayouts row). A save without it (1.0.0) loads saved made snow as a full pack
// (RunSnowRules.TrackFromSave). A 1.0.0 build skips this unknown type cleanly (ComponentSerializer.cs:51-73). SkiSnowInjectionSystem
// (RefreshRunSnow) adds and writes it beside SkiTrackSnow, and reads it once per track after a load. Layout v1 (5 bytes): it never
// changes, and no payload length is written. New fields go in a new type. Never rename the type or move its namespace without
// [FormerlySerializedAs].

using Colossal.Serialization.Entities;
using Unity.Entities;

namespace SkiResortMod.Systems
{
    // Stripped saved AQN as a literal. Rules: SkiRunBinding.cs "SELF-ALIAS"
    [FormerlySerializedAs("SkiResortMod.Systems.SkiTrackPack, SkiResortMod")]
    public struct SkiTrackPack : IComponentData, IQueryTypeParameter, ISerializable
    {
        /// <summary>Layout version written by this build.</summary>
        public const byte CurrentVersion = 1;

        /// <summary>Pack depth in millimeters (0..960: RunSnowRules.TrackDepthToMm writes it, TrackDepthFromMm reads and clamps it).</summary>
        public int m_DepthMm;

        /// <summary>Layout version this value was created with or loaded from (not written: Serialize writes CurrentVersion).</summary>
        public byte m_Version;

        public SkiTrackPack(int depthMm)
        {
            m_DepthMm = depthMm;
            m_Version = CurrentVersion;
        }

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            // Typed locals pin the IWriter.Write(byte) overload
            byte version = CurrentVersion;
            writer.Write(version);
            writer.Write(m_DepthMm);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            reader.Read(out m_Version);
            reader.Read(out m_DepthMm);
        }
    }
}
