// Dev-only probe mod (SkiTestMergeTag.csproj header). It registers nothing: the component type is the whole point.

using Colossal.Logging;
using Colossal.Serialization.Entities;
using Game;
using Game.Modding;
using Unity.Entities;

namespace SkiTestMergeTag
{
    public sealed class Mod : IMod
    {
        internal static readonly ILog Log = LogManager.GetLogger(nameof(SkiTestMergeTag)).SetShowsErrorsInUI(false);

        public void OnLoad(UpdateSystem updateSystem)
        {
            Log.Info("Loaded: MergeProbeTag is a saved component type.");
        }

        public void OnDispose()
        {
        }
    }

    /// <summary>Stands in for the planned SkiMergedLodge tag: a version byte and a serial, saved with the building.</summary>
    public struct MergeProbeTag : IComponentData, IQueryTypeParameter, ISerializable
    {
        public byte m_Version;
        public int m_Serial;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            byte version = 1;
            writer.Write(version);
            writer.Write(m_Serial);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            reader.Read(out m_Version);
            reader.Read(out m_Serial);
        }
    }
}
