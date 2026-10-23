// The few engine types the persisted component files (SkiRunBinding.cs, SkiRunTreeMember.cs, SkiSeasonPass.cs) name, re-declared with the
// same namespaces, names and member shapes so those files compile here unchanged. They live in tests/SaveBatch only: a shim of an engine
// namespace in a shared test host would let a "pure" file that accidentally uses Unity.Entities compile there.
// Shapes from the decompile (Colossal.Core): ISerializable.cs:3-8, FormerlySerializedAsAttribute.cs:5-13. IWriter / IReader carry only the
// scalar overloads, so a persisted field of another type fails to compile here on purpose: extend the shim and the codec in Program.cs
// together, from BinaryWriter.cs. Encoding = BinaryWriter.cs:128-141 (Entity: int32 saved index, -1 when not in the table; here
// Entity.Index is written as is, the identity table), :305-354 (int32 / int64 / double / byte little-endian, raw).

using System;

namespace Unity.Entities
{
    public interface IComponentData { }
    public interface IBufferElementData { }
    public interface IQueryTypeParameter { }

    public struct Entity : IEquatable<Entity>
    {
        public int Index;
        public int Version;
        public static Entity Null => default;
        public bool Equals(Entity other) => Index == other.Index && Version == other.Version;
        public override bool Equals(object obj) => obj is Entity e && Equals(e);
        public override int GetHashCode() => Index * 397 ^ Version;
        public override string ToString() => "Entity(" + Index + ":" + Version + ")";
    }
}

namespace Colossal.Serialization.Entities
{
    using Unity.Entities;

    public interface IWriter
    {
        void Write(byte value);
        void Write(int value);
        void Write(long value);
        void Write(double value);
        void Write(Entity value);
    }

    public interface IReader
    {
        void Read(out byte value);
        void Read(out int value);
        void Read(out long value);
        void Read(out double value);
        void Read(out Entity value);
    }

    public interface ISerializable
    {
        void Serialize<TWriter>(TWriter writer) where TWriter : IWriter;
        void Deserialize<TReader>(TReader reader) where TReader : IReader;
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = true, Inherited = false)]
    public class FormerlySerializedAsAttribute : Attribute
    {
        public string oldName { get; private set; }
        public FormerlySerializedAsAttribute(string oldName) { this.oldName = oldName; }
    }
}
