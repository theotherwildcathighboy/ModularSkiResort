// Serializable made-snow pack of a cat track (T220, 1.0.1): the depth its guns built on its made share (RunSnowRules.TrackMadeSnow
// .DepthM) and, from v2, whether the track was Open. SkiTrackSnow (1.0.0) saves the share only and its layout never changes, so the pack
// is this NEW type (production mode: a new persisted concept is a new type with a new SaveLayouts row). A save without it (1.0.0) loads
// saved made snow as a full pack (RunSnowRules.TrackFromSave). A 1.0.0 build skips this unknown type cleanly (ComponentSerializer.cs:51-73).
// SkiSnowInjectionSystem (RefreshRunSnow) adds and writes it beside SkiTrackSnow, and reads it after a load.
// Layouts: v1 (5 bytes) m_DepthMm. v2 (6 bytes) appends m_Flags (bit 0: the track was Open). v2 came before any release carried this type
// (1.0.0 has none; bridge pass r1d loadcheck d: a track saved Closed on a 0.35 m pack reloaded Open), so no released build reads a v2
// block it does not know. A v1 value (an unreleased 1.0.1 test save) loads with no saved state (StateSaved false). Once 1.0.1 ships, this
// layout never changes again (new fields go in a new type), and no payload length is written. Never rename the type or move its namespace
// without [FormerlySerializedAs].

using Colossal.Serialization.Entities;
using Unity.Entities;

namespace SkiResortMod.Systems
{
    // Stripped saved AQN as a literal. Rules: SkiRunBinding.cs "SELF-ALIAS"
    [FormerlySerializedAs("SkiResortMod.Systems.SkiTrackPack, SkiResortMod")]
    public struct SkiTrackPack : IComponentData, IQueryTypeParameter, ISerializable
    {
        /// <summary>Layout version written by this build.</summary>
        public const byte CurrentVersion = 2;

        /// <summary>m_Flags bit 0: the track's open state was Open when this value was written (else Closed).</summary>
        public const byte FlagOpen = 1;

        /// <summary>Pack depth in millimeters (0..960: RunSnowRules.TrackDepthToMm writes it, TrackDepthFromMm reads and clamps it).</summary>
        public int m_DepthMm;

        /// <summary>Layout version this value was created with or loaded from (not written: Serialize writes CurrentVersion).</summary>
        public byte m_Version;

        /// <summary>v2: FlagOpen. 0 in a v1 value (read as "no saved state", see StateSaved).</summary>
        public byte m_Flags;

        public SkiTrackPack(int depthMm, bool open)
        {
            m_DepthMm = depthMm;
            m_Flags = open ? FlagOpen : (byte)0;
            m_Version = CurrentVersion;
        }

        /// <summary>The value holds the track's open state (v2 or newer). A v1 value has the depth only.</summary>
        public bool StateSaved => m_Version >= 2;

        /// <summary>The saved open state was Open. Meaningful only when StateSaved.</summary>
        public bool SavedOpen => (m_Flags & FlagOpen) != 0;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            // Typed locals pin the IWriter.Write(byte) overload
            byte version = CurrentVersion;
            writer.Write(version);
            writer.Write(m_DepthMm);
            byte flags = m_Flags;
            writer.Write(flags);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            reader.Read(out m_Version);
            reader.Read(out m_DepthMm);
            m_Flags = 0;
            if (m_Version >= 2)
                reader.Read(out m_Flags);
        }
    }
}
