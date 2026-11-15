using System;
using System.Buffers;
using System.Collections.Generic;
using ApexPack.Buffers;

namespace ApexPack.Parallel;

/// <summary>
/// 大きなコレクションをチャンクに分けて CPU コアごとに並列に読み書きする。
/// </summary>
/// <remarks>
/// 形式: "APXP"(u32 LE) / 総件数(u32 LE) / チャンク数(u32 LE) / 各チャンクの開始位置(u32 LE × チャンク数) / 各チャンク本体。
/// チャンク本体は要素を null 可の位置として連結したもの。
/// </remarks>
public static class ApexPackParallel
{
    private const uint MagicParallelHeader = 0x50585041; // "APXP"

    public static byte[] Serialize<T>(IReadOnlyList<T> items, int chunkSize = 4096)
        where T : ISerializePackable<T>
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(chunkSize);
        int totalCount = items.Count;
        int chunkCount = totalCount == 0 ? 0 : (totalCount + chunkSize - 1) / chunkSize;
        var chunks = new byte[chunkCount][];

        System.Threading.Tasks.Parallel.For(0, chunkCount, chunkIndex =>
        {
            int start = chunkIndex * chunkSize;
            int count = Math.Min(chunkSize, totalCount - start);
            var writer = new ApexBufferWriter(count * 32);
            try
            {
                var tracker = new NullReferenceTracker();
                for (int i = 0; i < count; i++)
                {
                    var item = items[start + i];
                    T.SerializeWithTracker(ref writer, in item, ref tracker);
                }
                chunks[chunkIndex] = writer.ToArray();
            }
            finally
            {
                writer.Dispose();
            }
        });

        int headerSize = checked(12 + chunkCount * 4);
        long total = headerSize;
        foreach (var chunk in chunks) total += chunk.Length;
        var result = GC.AllocateUninitializedArray<byte>(checked((int)total));

        var header = new ApexSpanWriter(result);
        header.WriteUInt32(MagicParallelHeader);
        header.WriteInt32(totalCount);
        header.WriteInt32(chunkCount);
        int offset = headerSize;
        foreach (var chunk in chunks)
        {
            header.WriteInt32(offset);
            chunk.CopyTo(result.AsSpan(offset));
            offset += chunk.Length;
        }
        return result;
    }

    public static T[] Deserialize<T>(byte[] bytes, int chunkSize = 4096)
        where T : ISerializePackable<T>
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(chunkSize);
        var reader = new ApexSpanReader(bytes);
        if (reader.ReadUInt32() != MagicParallelHeader)
        {
            throw new InvalidOperationException("Invalid parallel serialized data stream.");
        }

        int totalCount = reader.ReadInt32();
        int chunkCount = reader.ReadInt32();
        if (totalCount < 0 || chunkCount < 0 || chunkCount > reader.Remaining / 4
            || (long)chunkCount * chunkSize < totalCount || (chunkCount > 0 && (long)(chunkCount - 1) * chunkSize >= totalCount))
        {
            ApexThrowHelper.ThrowInvalidLength(totalCount);
        }
        if (totalCount == 0) return Array.Empty<T>();

        var offsets = new int[chunkCount + 1];
        for (int i = 0; i < chunkCount; i++)
        {
            offsets[i] = reader.ReadInt32();
        }
        offsets[chunkCount] = bytes.Length;
        for (int i = 0; i < chunkCount; i++)
        {
            if (offsets[i] < reader.Consumed || offsets[i] > offsets[i + 1]) ApexThrowHelper.ThrowInvalidLength(offsets[i]);
        }

        var result = new T[totalCount];
        System.Threading.Tasks.Parallel.For(0, chunkCount, chunkIndex =>
        {
            int startItem = chunkIndex * chunkSize;
            int count = Math.Min(chunkSize, totalCount - startItem);
            var chunkReader = new ApexSpanReader(bytes.AsSpan(offsets[chunkIndex], offsets[chunkIndex + 1] - offsets[chunkIndex]));
            var tracker = new NullReferenceTracker();
            var guard = new TrustedSecurityGuard();
            for (int i = 0; i < count; i++)
            {
                T.DeserializeGuarded(ref chunkReader, out result[startItem + i], ref tracker, ref guard);
            }
        });
        return result;
    }
}
