using System;
using System.Buffers;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ApexPack.Buffers;

namespace ApexPack;

/// <summary>
/// 連続領域の要素列の読み書き。固定長要素は領域確保 1 回で済ませ、LE 機かつメモリ表現が正規形なら一括コピーする。
/// </summary>
public static class ApexSequence
{
    /// <summary>件数ヘッダー(count + 1)と要素を書く。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteSpan<TWriter, TTracker, T, TFormatter>(ref TWriter writer, scoped ReadOnlySpan<T> span, ref TTracker tracker)
        where TWriter : struct, IApexBufferWriter, allows ref struct
        where TTracker : IReferenceTracker, allows ref struct
        where TFormatter : IApexFormatter<T>
    {
        if (TFormatter.FixedSize >= 0)
        {
            WriteFixedSpan<TWriter, T, TFormatter>(ref writer, span);
            return;
        }

        writer.WriteCollectionHeader(span.Length);
        for (int i = 0; i < span.Length; i++)
        {
            TFormatter.Write(ref writer, in span[i], ref tracker);
        }
    }

    /// <summary>件数ヘッダーなしで要素だけを書く(件数を別に書いた場合)。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteElements<TWriter, TTracker, T, TFormatter>(ref TWriter writer, scoped ReadOnlySpan<T> span, ref TTracker tracker)
        where TWriter : struct, IApexBufferWriter, allows ref struct
        where TTracker : IReferenceTracker, allows ref struct
        where TFormatter : IApexFormatter<T>
    {
        int size = TFormatter.FixedSize;
        if (size >= 0)
        {
            int body = checked(span.Length * size);
            ref byte dst = ref writer.GetSpanRef(body);
            CopyFixedOut<T, TFormatter>(ref dst, span, TWriter.IsHostLittleEndian);
            return;
        }
        for (int i = 0; i < span.Length; i++)
        {
            TFormatter.Write(ref writer, in span[i], ref tracker);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteFixedSpan<TWriter, T, TFormatter>(ref TWriter writer, scoped ReadOnlySpan<T> span)
        where TWriter : struct, IApexBufferWriter, allows ref struct
        where TFormatter : IApexFormatter<T>
    {
        bool le = TWriter.IsHostLittleEndian;
        int count = span.Length;
        int body = checked(count * TFormatter.FixedSize);
        // ヘッダーの長さは件数から決まるので、ちょうどの大きさを確保する(正確なサイズの配列へ直接書く経路でも足りるように)
        int headerSize = ApexWire.CompactSize((uint)count + 1);
        ref byte p = ref writer.GetSpanRef(checked(headerSize + body));
        ApexWire.WriteCompact(ref p, (uint)count + 1, le);
        CopyFixedOut<T, TFormatter>(ref Unsafe.Add(ref p, headerSize), span, le);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void CopyFixedOut<T, TFormatter>(ref byte dst, scoped ReadOnlySpan<T> span, bool le)
        where TFormatter : IApexFormatter<T>
    {
        int size = TFormatter.FixedSize;
        if (le && TFormatter.IsRawLayout)
        {
            if (span.Length > 0)
            {
                Unsafe.CopyBlockUnaligned(ref dst, ref Unsafe.As<T, byte>(ref MemoryMarshal.GetReference(span)), (uint)(span.Length * size));
            }
            return;
        }
        if (le && size > 0 && TFormatter.RawPrefixSize == size)
        {
            // メモリの先頭はワイヤと同じで、後ろに詰め物がある型(例: (int, int, int))。詰め物を飛ばしてまとめて写す
            ApexWire.PackStrided(ref dst, ref Unsafe.As<T, byte>(ref MemoryMarshal.GetReference(span)), span.Length, Unsafe.SizeOf<T>(), size);
            return;
        }
        for (int i = 0; i < span.Length; i++)
        {
            TFormatter.WriteFixed(ref Unsafe.Add(ref dst, i * size), in span[i], le);
        }
    }

    /// <summary>件数が分かった後で要素を <paramref name="destination"/> に読む。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ReadElements<TReader, TTracker, TGuard, T, TFormatter>(ref TReader reader, scoped Span<T> destination, ref TTracker tracker, ref TGuard guard)
        where TReader : struct, IApexBufferReader, allows ref struct
        where TTracker : IReferenceTracker, allows ref struct
        where TGuard : ISecurityGuard, allows ref struct
        where TFormatter : IApexFormatter<T>
    {
        int size = TFormatter.FixedSize;
        if (size >= 0)
        {
            int body = checked(destination.Length * size);
            ref readonly byte src = ref reader.GetSpanRef(body);
            bool le = TReader.IsHostLittleEndian;
            if (le && TFormatter.IsRawLayout)
            {
                if (body > 0)
                {
                    Unsafe.CopyBlockUnaligned(ref Unsafe.As<T, byte>(ref MemoryMarshal.GetReference(destination)), ref Unsafe.AsRef(in src), (uint)body);
                }
                return;
            }
            if (le && size > 0 && TFormatter.RawPrefixSize == size)
            {
                ApexWire.UnpackStrided(ref Unsafe.As<T, byte>(ref MemoryMarshal.GetReference(destination)), ref Unsafe.AsRef(in src), destination.Length, Unsafe.SizeOf<T>(), size);
                return;
            }
            for (int i = 0; i < destination.Length; i++)
            {
                destination[i] = TFormatter.ReadFixed(in Unsafe.Add(ref Unsafe.AsRef(in src), i * size), le);
            }
            return;
        }

        // 参照型の要素は共有ジェネリックコードになり、要素ごとの呼び出しに実行時の検索が入る。
        // よく使う組み合わせ(SpanReader・追跡なし・信頼済み)では、型を具体的に書いた生成コードにまとめて読ませる
        if (!typeof(T).IsValueType && typeof(TReader) == typeof(ApexSpanReader) && typeof(TTracker) == typeof(NullReferenceTracker)
            && typeof(TGuard) == typeof(TrustedSecurityGuard) && TFormatter.ReadsLikeRoot && destination.Length > 1
            && ApexFormatterCache<T>.Instance.TryReadElements(ref Unsafe.As<TReader, ApexSpanReader>(ref reader), destination))
        {
            return;
        }

        for (int i = 0; i < destination.Length; i++)
        {
            destination[i] = TFormatter.Read(ref reader, ref tracker, ref guard);
        }
    }

    /// <summary>件数ヘッダーを読む(null は -1)。要素の最小サイズで割り当て爆弾を事前に拒否する。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ReadCount<TReader, TGuard, T, TFormatter>(ref TReader reader, ref TGuard guard)
        where TReader : struct, IApexBufferReader, allows ref struct
        where TGuard : ISecurityGuard, allows ref struct
        where TFormatter : IApexFormatter<T>
        => reader.ReadCollectionHeader(ref guard, TFormatter.MinWireSize);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T[] AllocateArray<T, TFormatter>(int count)
        where TFormatter : IApexFormatter<T>
    {
        if (count == 0) return Array.Empty<T>();
        return TFormatter.FixedSize >= 0 && !RuntimeHelpers.IsReferenceOrContainsReferences<T>()
            ? GC.AllocateUninitializedArray<T>(count)
            : new T[count];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T[]? ReadArray<TReader, TTracker, TGuard, T, TFormatter>(ref TReader reader, ref TTracker tracker, ref TGuard guard)
        where TReader : struct, IApexBufferReader, allows ref struct
        where TTracker : IReferenceTracker, allows ref struct
        where TGuard : ISecurityGuard, allows ref struct
        where TFormatter : IApexFormatter<T>
    {
        int count = ReadCount<TReader, TGuard, T, TFormatter>(ref reader, ref guard);
        if (count < 0) return null;
        if (count == 1 && !typeof(T).IsValueType && TFormatter.FixedSize < 0)
        {
            var single = new T[1];
            // ここで確保した正確な型の配列へ書くため、共変配列の型チェックは不要。
            MemoryMarshal.GetArrayDataReference(single) = TFormatter.Read(ref reader, ref tracker, ref guard);
            return single;
        }
        var array = AllocateArray<T, TFormatter>(count);
        ReadElements<TReader, TTracker, TGuard, T, TFormatter>(ref reader, array, ref tracker, ref guard);
        return array;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static List<T>? ReadList<TReader, TTracker, TGuard, T, TFormatter>(ref TReader reader, ref TTracker tracker, ref TGuard guard)
        where TReader : struct, IApexBufferReader, allows ref struct
        where TTracker : IReferenceTracker, allows ref struct
        where TGuard : ISecurityGuard, allows ref struct
        where TFormatter : IApexFormatter<T>
    {
        int count = ReadCount<TReader, TGuard, T, TFormatter>(ref reader, ref guard);
        if (count < 0) return null;
        var list = new List<T>(count);
        CollectionsMarshal.SetCount(list, count);
        ReadElements<TReader, TTracker, TGuard, T, TFormatter>(ref reader, CollectionsMarshal.AsSpan(list), ref tracker, ref guard);
        return list;
    }

    internal const int SmallCollectionCapacity = 8;

    [InlineArray(SmallCollectionCapacity)]
    private struct SmallCollectionBuffer<T>
    {
        private T _first;
    }

    // 大きな構造体の読み取り経路に、一時領域のスタック確保を持ち込まない。
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static Stack<T> ReadSmallStack<TReader, TTracker, TGuard, T, TFormatter>(ref TReader reader, int count, ref TTracker tracker, ref TGuard guard)
        where TReader : struct, IApexBufferReader, allows ref struct
        where TTracker : IReferenceTracker, allows ref struct
        where TGuard : ISecurityGuard, allows ref struct
        where TFormatter : IApexFormatter<T>
    {
        SmallCollectionBuffer<T> buffer = default;
        Span<T> elements = buffer[..count];
        ReadElements<TReader, TTracker, TGuard, T, TFormatter>(ref reader, elements, ref tracker, ref guard);

        var stack = new Stack<T>(count);
        for (int i = elements.Length - 1; i >= 0; i--)
        {
            stack.Push(elements[i]);
        }
        return stack;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static FrozenSet<T> ReadSmallFrozenSet<TReader, TTracker, TGuard, T, TFormatter>(ref TReader reader, int count, ref TTracker tracker, ref TGuard guard)
        where TReader : struct, IApexBufferReader, allows ref struct
        where TTracker : IReferenceTracker, allows ref struct
        where TGuard : ISecurityGuard, allows ref struct
        where TFormatter : IApexFormatter<T>
    {
        SmallCollectionBuffer<T> buffer = default;
        Span<T> elements = buffer[..count];
        ReadElements<TReader, TTracker, TGuard, T, TFormatter>(ref reader, elements, ref tracker, ref guard);
        return FrozenSet.Create<T>(elements);
    }

    /// <summary>件数が既知の列挙を書く(件数とは別に要素数が食い違えば例外)。</summary>
    public static void WriteCollection<TWriter, TTracker, T, TFormatter>(ref TWriter writer, ICollection<T> source, ref TTracker tracker)
        where TWriter : struct, IApexBufferWriter, allows ref struct
        where TTracker : IReferenceTracker, allows ref struct
        where TFormatter : IApexFormatter<T>
    {
        int count = source.Count;
        if (count <= 0)
        {
            writer.WriteCollectionHeader(0);
            return;
        }
        // インターフェースの列挙子(箱詰めされる)を使わず、借りた配列へ CopyTo で一度に写してから書く。
        // 固定長の要素は一括コピーになる。参照を含む要素は返す前に消す(利用者のオブジェクトを共有プールに残さない)
        T[] rented = ArrayPool<T>.Shared.Rent(count);
        try
        {
            source.CopyTo(rented, 0);
            WriteSpan<TWriter, TTracker, T, TFormatter>(ref writer, new ReadOnlySpan<T>(rented, 0, count), ref tracker);
        }
        catch (ArgumentException)
        {
            // CopyTo の途中で件数が変わった(書き込みと並行して変更された)
            ThrowCollectionModified();
        }
        finally
        {
            ArrayPool<T>.Shared.Return(rented, clearArray: RuntimeHelpers.IsReferenceOrContainsReferences<T>());
        }
    }

    /// <summary>任意の列挙を書く。件数が取れない場合は一度配列化する。</summary>
    public static void WriteEnumerable<TWriter, TTracker, T, TFormatter>(ref TWriter writer, IEnumerable<T>? source, ref TTracker tracker)
        where TWriter : struct, IApexBufferWriter, allows ref struct
        where TTracker : IReferenceTracker, allows ref struct
        where TFormatter : IApexFormatter<T>
    {
        switch (source)
        {
            case null:
                writer.WriteNullCollectionHeader();
                return;
            case T[] array:
                WriteSpan<TWriter, TTracker, T, TFormatter>(ref writer, array, ref tracker);
                return;
            case List<T> list:
                WriteSpan<TWriter, TTracker, T, TFormatter>(ref writer, CollectionsMarshal.AsSpan(list), ref tracker);
                return;
            case ICollection<T> collection:
                WriteCollection<TWriter, TTracker, T, TFormatter>(ref writer, collection, ref tracker);
                return;
            default:
                var buffer = new List<T>(source);
                WriteSpan<TWriter, TTracker, T, TFormatter>(ref writer, CollectionsMarshal.AsSpan(buffer), ref tracker);
                return;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void ThrowCollectionModified()
    {
        throw new InvalidOperationException("Collection was modified during serialization.");
    }
}
