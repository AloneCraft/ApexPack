using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ApexPack.Buffers;

namespace ApexPack;

/// <summary>
/// Primary entry point for ApexPack serialization and deserialization.
/// </summary>
public static class ApexPackSerializer
{
    [ThreadStatic]
    private static byte[]? t_threadBuffer;

    private const int ThreadBufferSize = 65536;

    /// <summary>
    /// Serializes a value into a byte array with thread-local buffer fast-path.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte[] Serialize<T>(scoped in T value) where T : ISerializePackable<T>
    {
        var buffer = t_threadBuffer ??= new byte[ThreadBufferSize];
        var writer = new ApexSpanWriter(buffer);
        try
        {
            T.Serialize(ref writer, in value);
            return writer.WrittenSpan.ToArray();
        }
        catch (InvalidOperationException)
        {
            var expandingWriter = new ApexBufferWriter(ThreadBufferSize * 2);
            try
            {
                T.Serialize(ref expandingWriter, in value);
                return expandingWriter.ToArray();
            }
            finally
            {
                expandingWriter.Dispose();
            }
        }
    }

    /// <summary>
    /// Serializes a value directly into a target Span&lt;byte&gt; (100% zero heap allocation).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Serialize<T>(Span<byte> destination, scoped in T value) where T : ISerializePackable<T>
    {
        var writer = new ApexSpanWriter(destination);
        T.Serialize(ref writer, in value);
        return writer.BytesWritten;
    }

    /// <summary>
    /// Deserializes a value from a ReadOnlySpan&lt;byte&gt; (zero allocation for structs).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T Deserialize<T>(ReadOnlySpan<byte> bytes) where T : ISerializePackable<T>
    {
        var reader = new ApexSpanReader(bytes);
        T.Deserialize(ref reader, out var value);
        return value;
    }

    /// <summary>
    /// Serializes an object graph with circular references and shared instance tracking enabled.
    /// </summary>
    public static byte[] SerializeWithReferences<T>(scoped in T value) where T : ISerializePackable<T>
    {
        var writer = new ApexBufferWriter(256);
        var tracker = new ActiveReferenceTracker();
        try
        {
            T.SerializeWithTracker(ref writer, in value, ref tracker);
            return writer.ToArray();
        }
        finally
        {
            tracker.Dispose();
            writer.Dispose();
        }
    }

    /// <summary>
    /// Deserializes an object graph with circular references and shared instance tracking enabled.
    /// </summary>
    public static T DeserializeWithReferences<T>(ReadOnlySpan<byte> bytes) where T : ISerializePackable<T>
    {
        var reader = new ApexSpanReader(bytes);
        var tracker = new ActiveReferenceTracker();
        try
        {
            T.DeserializeWithTracker(ref reader, out var value, ref tracker);
            return value;
        }
        finally
        {
            tracker.Dispose();
        }
    }

    /// <summary>
    /// Serializes an array of packable items.
    /// </summary>
    public static void SerializeArray<T, TWriter>(ref TWriter writer, T[]? items)
        where T : ISerializePackable<T>
        where TWriter : IApexBufferWriter, allows ref struct
    {
        if (items is null)
        {
            writer.WriteInt32(-1);
            return;
        }

        int count = items.Length;
        writer.WriteInt32(count);
        for (int i = 0; i < count; i++)
        {
            T.Serialize(ref writer, in items[i]);
        }
    }

    /// <summary>
    /// Deserializes an array of packable items.
    /// </summary>
    public static T[]? DeserializeArray<T, TReader>(ref TReader reader)
        where T : ISerializePackable<T>
        where TReader : IApexBufferReader, allows ref struct
    {
        int count = reader.ReadInt32();
        if (count == -1) return null;
        if (count == 0) return Array.Empty<T>();

        var array = new T[count];
        for (int i = 0; i < count; i++)
        {
            T.Deserialize(ref reader, out array[i]);
        }
        return array;
    }

    /// <summary>
    /// Serializes a generic List&lt;T&gt; of packable items.
    /// </summary>
    public static void SerializeList<T, TWriter>(ref TWriter writer, List<T>? items)
        where T : ISerializePackable<T>
        where TWriter : IApexBufferWriter, allows ref struct
    {
        if (items is null)
        {
            writer.WriteInt32(-1);
            return;
        }

        int count = items.Count;
        writer.WriteInt32(count);
        for (int i = 0; i < count; i++)
        {
            var item = items[i];
            T.Serialize(ref writer, in item);
        }
    }

    /// <summary>
    /// Deserializes a generic List&lt;T&gt; of packable items.
    /// </summary>
    public static List<T>? DeserializeList<T, TReader>(ref TReader reader)
        where T : ISerializePackable<T>
        where TReader : IApexBufferReader, allows ref struct
    {
        int count = reader.ReadInt32();
        if (count == -1) return null;
        if (count == 0) return new List<T>(0);

        var list = new List<T>(count);
        for (int i = 0; i < count; i++)
        {
            T.Deserialize(ref reader, out var item);
            list.Add(item);
        }
        return list;
    }

    /// <summary>
    /// Serializes an unmanaged array using direct fast block copy.
    /// </summary>
    public static void SerializeUnmanagedArray<T, TWriter>(ref TWriter writer, T[]? items)
        where T : unmanaged
        where TWriter : IApexBufferWriter, allows ref struct
    {
        if (items is null)
        {
            writer.WriteInt32(-1);
            return;
        }

        int count = items.Length;
        writer.WriteInt32(count);
        if (count > 0)
        {
            var byteSpan = MemoryMarshal.AsBytes(items.AsSpan());
            writer.WriteRaw(byteSpan);
        }
    }

    /// <summary>
    /// Deserializes an unmanaged array using direct fast block copy.
    /// </summary>
    public static T[]? DeserializeUnmanagedArray<T, TReader>(ref TReader reader)
        where T : unmanaged
        where TReader : IApexBufferReader, allows ref struct
    {
        int count = reader.ReadInt32();
        if (count == -1) return null;
        if (count == 0) return Array.Empty<T>();

        var array = new T[count];
        var byteSpan = MemoryMarshal.AsBytes(array.AsSpan());
        var sourceSpan = reader.ReadRaw(byteSpan.Length);
        sourceSpan.CopyTo(byteSpan);
        return array;
    }

    /// <summary>
    /// Serializes an unmanaged List&lt;T&gt; using direct fast block copy.
    /// </summary>
    public static void SerializeUnmanagedList<T, TWriter>(ref TWriter writer, List<T>? items)
        where T : unmanaged
        where TWriter : IApexBufferWriter, allows ref struct
    {
        if (items is null)
        {
            writer.WriteInt32(-1);
            return;
        }

        int count = items.Count;
        writer.WriteInt32(count);
        if (count > 0)
        {
            var span = CollectionsMarshal.AsSpan(items);
            var byteSpan = MemoryMarshal.AsBytes(span);
            writer.WriteRaw(byteSpan);
        }
    }

    /// <summary>
    /// Deserializes an unmanaged List&lt;T&gt; using direct fast block copy.
    /// </summary>
    public static List<T>? DeserializeUnmanagedList<T, TReader>(ref TReader reader)
        where T : unmanaged
        where TReader : IApexBufferReader, allows ref struct
    {
        int count = reader.ReadInt32();
        if (count == -1) return null;
        if (count == 0) return new List<T>(0);

        var list = new List<T>(count);
        CollectionsMarshal.SetCount(list, count);
        var span = CollectionsMarshal.AsSpan(list);
        var byteSpan = MemoryMarshal.AsBytes(span);
        var sourceSpan = reader.ReadRaw(byteSpan.Length);
        sourceSpan.CopyTo(byteSpan);
        return list;
    }
}
