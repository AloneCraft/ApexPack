using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using ApexPack.Buffers;

namespace ApexPack.Parallel;

/// <summary>
/// Multi-core parallel chunking serialization and deserialization engine.
/// Scales linearly across CPU cores for large datasets.
/// </summary>
public static class ApexPackParallel
{
    private const uint MagicParallelHeader = 0x50585041; // "APXP" in little-endian

    /// <summary>
    /// Serializes a collection in parallel across CPU cores using chunking.
    /// </summary>
    public static byte[] Serialize<T>(IReadOnlyList<T> items, int chunkSize = 4096)
        where T : ISerializePackable<T>
    {
        int totalCount = items.Count;
        if (totalCount == 0)
        {
            var emptyWriter = new ApexBufferWriter(32);
            emptyWriter.WriteUInt32(MagicParallelHeader);
            emptyWriter.WriteInt32(0);
            emptyWriter.WriteInt32(0);
            return emptyWriter.ToArray();
        }

        int chunkCount = (totalCount + chunkSize - 1) / chunkSize;
        var chunkBuffers = new byte[chunkCount][];
        var chunkLengths = new int[chunkCount];

        System.Threading.Tasks.Parallel.For(0, chunkCount, chunkIndex =>
        {
            int start = chunkIndex * chunkSize;
            int count = Math.Min(chunkSize, totalCount - start);

            var writer = new ApexBufferWriter(count * 32);
            try
            {
                for (int i = 0; i < count; i++)
                {
                    var item = items[start + i];
                    T.Serialize(ref writer, in item);
                }
                chunkBuffers[chunkIndex] = writer.ToArray();
                chunkLengths[chunkIndex] = writer.BytesWritten;
            }
            finally
            {
                writer.Dispose();
            }
        });

        // Compute offsets
        int headerSize = sizeof(uint) + sizeof(int) + sizeof(int) + (chunkCount * sizeof(int));
        int totalPayloadSize = 0;
        for (int i = 0; i < chunkCount; i++)
        {
            totalPayloadSize += chunkLengths[i];
        }

        byte[] finalBuffer = new byte[headerSize + totalPayloadSize];
        var span = finalBuffer.AsSpan();

        // Write header
        var headerWriter = new ApexSpanWriter(span);
        headerWriter.WriteUInt32(MagicParallelHeader);
        headerWriter.WriteInt32(totalCount);
        headerWriter.WriteInt32(chunkCount);

        int currentOffset = headerSize;
        for (int i = 0; i < chunkCount; i++)
        {
            headerWriter.WriteInt32(currentOffset);
            currentOffset += chunkLengths[i];
        }

        // Scatter-gather chunk copying
        currentOffset = headerSize;
        for (int i = 0; i < chunkCount; i++)
        {
            chunkBuffers[i].AsSpan(0, chunkLengths[i]).CopyTo(span.Slice(currentOffset, chunkLengths[i]));
            currentOffset += chunkLengths[i];
        }

        return finalBuffer;
    }

    /// <summary>
    /// Deserializes a parallel-serialized collection across CPU cores without lock contention.
    /// </summary>
    public static T[] Deserialize<T>(byte[] bytes, int chunkSize = 4096)
        where T : ISerializePackable<T>
    {
        var reader = new ApexSpanReader(bytes);
        uint magic = reader.ReadUInt32();
        if (magic != MagicParallelHeader)
        {
            throw new InvalidOperationException("Invalid parallel serialized data stream.");
        }

        int totalCount = reader.ReadInt32();
        int chunkCount = reader.ReadInt32();

        if (totalCount == 0 || chunkCount == 0)
        {
            return Array.Empty<T>();
        }

        var offsets = new int[chunkCount];
        for (int i = 0; i < chunkCount; i++)
        {
            offsets[i] = reader.ReadInt32();
        }

        var result = new T[totalCount];

        System.Threading.Tasks.Parallel.For(0, chunkCount, chunkIndex =>
        {
            int startItem = chunkIndex * chunkSize;
            int count = Math.Min(chunkSize, totalCount - startItem);

            int startOffset = offsets[chunkIndex];
            int nextOffset = (chunkIndex == chunkCount - 1) ? bytes.Length : offsets[chunkIndex + 1];
            int chunkLength = nextOffset - startOffset;

            var chunkSlice = bytes.AsSpan().Slice(startOffset, chunkLength);
            var chunkReader = new ApexSpanReader(chunkSlice);

            for (int i = 0; i < count; i++)
            {
                T.Deserialize(ref chunkReader, out result[startItem + i]);
            }
        });

        return result;
    }
}
