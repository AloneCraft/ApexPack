using System.Runtime.CompilerServices;
using ApexPack.Buffers;

namespace ApexPack;

/// <summary>
/// Core contract for types serializable by ApexPack using static abstract methods.
/// Provides zero-allocation, zero-virtual-call, and zero beforefieldinit JIT check serialization.
/// </summary>
public interface ISerializePackable<T> where T : ISerializePackable<T>
{
    /// <summary>
    /// Default high-performance serialization (Zero-cost: no reference tracking).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static virtual void Serialize<TWriter>(ref TWriter writer, scoped in T value)
        where TWriter : IApexBufferWriter, allows ref struct
    {
        var nullTracker = new NullReferenceTracker();
        T.SerializeWithTracker(ref writer, in value, ref nullTracker);
    }

    /// <summary>
    /// Default high-performance deserialization (Zero-cost: no reference tracking).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static virtual void Deserialize<TReader>(ref TReader reader, scoped out T value)
        where TReader : IApexBufferReader, allows ref struct
    {
        var nullTracker = new NullReferenceTracker();
        T.DeserializeWithTracker(ref reader, out value, ref nullTracker);
    }

    /// <summary>
    /// Serialization supporting optional reference tracking (circular references and instance sharing).
    /// </summary>
    static abstract void SerializeWithTracker<TWriter, TTracker>(ref TWriter writer, scoped in T value, ref TTracker tracker)
        where TWriter : IApexBufferWriter, allows ref struct
        where TTracker : IReferenceTracker, allows ref struct;

    /// <summary>
    /// Deserialization supporting optional reference tracking (circular references and instance sharing).
    /// </summary>
    static abstract void DeserializeWithTracker<TReader, TTracker>(ref TReader reader, scoped out T value, ref TTracker tracker)
        where TReader : IApexBufferReader, allows ref struct
        where TTracker : IReferenceTracker, allows ref struct;
}
