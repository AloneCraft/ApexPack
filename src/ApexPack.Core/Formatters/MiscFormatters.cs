using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using ApexPack.Buffers;

namespace ApexPack;

internal static class DictionaryHelper
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int CheckCount<TReader, TGuard>(ref TReader reader, ref TGuard guard, uint count, int minPairSize)
        where TReader : struct, IApexBufferReader, allows ref struct
        where TGuard : ISecurityGuard, allows ref struct
    {
        if (count > int.MaxValue || (ulong)count * (uint)minPairSize > (uint)reader.Remaining)
        {
            ApexThrowHelper.ThrowInvalidLength((int)Math.Min(count, int.MaxValue));
        }
        guard.AddElements((int)count);
        return (int)count;
    }

    /// <summary>多次元配列の総要素数を検査する。</summary>
    public static int CheckTotal<TReader, TGuard>(ref TReader reader, ref TGuard guard, scoped ReadOnlySpan<uint> lengths, int minElementSize)
        where TReader : struct, IApexBufferReader, allows ref struct
        where TGuard : ISecurityGuard, allows ref struct
    {
        ulong total = 1;
        foreach (uint length in lengths)
        {
            total *= length;
            if (total > int.MaxValue) ApexThrowHelper.ThrowInvalidLength(int.MaxValue);
        }
        if (total * (uint)minElementSize > (uint)reader.Remaining) ApexThrowHelper.ThrowInvalidLength((int)total);
        guard.AddElements((int)total);
        return (int)total;
    }
}

// ======================================================================
// Nullable 要素の配列・List: 件数ヘッダー + 存在ビットマップ + 存在する値だけ
// ======================================================================

internal static class NullableSequence
{
    public static void Write<TWriter, TTracker, T, TF>(ref TWriter writer, scoped ReadOnlySpan<T?> span, ref TTracker tracker)
        where TWriter : struct, IApexBufferWriter, allows ref struct
        where TTracker : IReferenceTracker, allows ref struct
        where T : struct
        where TF : IApexFormatter<T>
    {
        bool le = TWriter.IsHostLittleEndian;
        int count = span.Length;
        int bitmapBytes = (count + 7) >> 3;
        int size = TF.FixedSize;

        if (size >= 0)
        {
            bool vector = le && TF.IsRawLayout && NullableVector<T>.ValueSize == size;
            // ベクトル経路は 16 バイト単位で書くので、末尾に余裕を取る
            int max = checked(ApexWire.CompactMaxSize + bitmapBytes + count * size + 16);
            ref byte p = ref writer.Reserve(max);
            int header = ApexWire.WriteCompact(ref p, (uint)count + 1, le);
            ref byte bitmap = ref Unsafe.Add(ref p, header);
            ref byte values = ref Unsafe.Add(ref bitmap, bitmapBytes);
            ref T? src = ref MemoryMarshal.GetReference(span);
            int offset = 0;
            int start = 0;
            if (vector)
            {
                offset = NullableVector<T>.Write(ref src, count, ref bitmap, ref values, out start);
            }
            for (int b = start >> 3, i = start; b < bitmapBytes; b++, i += 8)
            {
                // 1 パス目: 8 要素ぶんの存在ビット(要素間に依存がない)
                int n = Math.Min(8, count - i);
                uint presence = 0;
                for (int j = 0; j < n; j++)
                {
                    presence |= (Unsafe.Add(ref src, i + j).HasValue ? 1u : 0u) << j;
                }
                Unsafe.Add(ref bitmap, b) = (byte)presence;

                // 2 パス目: 立っているビットだけをたどって値を書く(依存は書き込み位置の加算だけ)
                while (presence != 0)
                {
                    int j = BitOperations.TrailingZeroCount(presence);
                    TF.WriteFixed(ref Unsafe.Add(ref values, offset), Unsafe.Add(ref src, i + j).GetValueOrDefault(), le);
                    offset += size;
                    presence &= presence - 1;
                }
            }
            writer.Advance(header + bitmapBytes + offset);
            return;
        }

        writer.WriteCollectionHeader(count);
        ref byte bits = ref writer.GetSpanRef(bitmapBytes);
        Unsafe.InitBlockUnaligned(ref bits, 0, (uint)bitmapBytes);
        for (int i = 0; i < count; i++)
        {
            if (span[i].HasValue) Unsafe.Add(ref bits, i >> 3) |= (byte)(1 << (i & 7));
        }
        for (int i = 0; i < count; i++)
        {
            if (span[i].HasValue)
            {
                T v = span[i].GetValueOrDefault();
                TF.Write(ref writer, in v, ref tracker);
            }
        }
    }

    /// <summary>件数ヘッダーを読む。ビットマップ分のバイトが残っていることも確かめる。</summary>
    public static int ReadCount<TReader, TGuard>(ref TReader reader, ref TGuard guard)
        where TReader : struct, IApexBufferReader, allows ref struct
        where TGuard : ISecurityGuard, allows ref struct
    {
        int count = reader.ReadCollectionHeader(ref guard, 0);
        if (count > 0 && ((count + 7) >> 3) > reader.Remaining) ApexThrowHelper.ThrowInvalidLength(count);
        return count;
    }

    public static void ReadElements<TReader, TTracker, TGuard, T, TF>(ref TReader reader, scoped Span<T?> destination, ref TTracker tracker, ref TGuard guard)
        where TReader : struct, IApexBufferReader, allows ref struct
        where TTracker : IReferenceTracker, allows ref struct
        where TGuard : ISecurityGuard, allows ref struct
        where T : struct
        where TF : IApexFormatter<T>
    {
        bool le = TReader.IsHostLittleEndian;
        int count = destination.Length;
        int bitmapBytes = (count + 7) >> 3;
        ref readonly byte bitmap = ref reader.GetSpanRef(bitmapBytes);
        int size = TF.FixedSize;

        if (size >= 0)
        {
            ref byte bits = ref Unsafe.AsRef(in bitmap);
            int present = 0;
            for (int i = 0; i < bitmapBytes; i++) present += BitOperations.PopCount(Unsafe.Add(ref bits, i));
            // 最後のバイトの余りビットが立っていたら不正(存在数と値の領域が食い違う)
            if ((count & 7) != 0 && (Unsafe.Add(ref bits, bitmapBytes - 1) >> (count & 7)) != 0) ApexThrowHelper.ThrowInvalidLength(count);
            ref byte values = ref Unsafe.AsRef(in reader.GetSpanRef(checked(present * size)));
            ref T? dst = ref MemoryMarshal.GetReference(destination);
            int offset = 0;
            int start = 0;
            if (le && TF.IsRawLayout && NullableVector<T>.ValueSize == size)
            {
                // ベクトル経路は要素を丸ごと(null も)書く
                offset = NullableVector<T>.Read(ref dst, count, ref bits, ref values, present * size, out start);
            }
            // 残りは null にしてから値のある要素だけ書く
            destination.Slice(start).Clear();
            for (int b = start >> 3, i = start; b < bitmapBytes; b++, i += 8)
            {
                uint presence = Unsafe.Add(ref bits, b);
                while (presence != 0)
                {
                    int j = BitOperations.TrailingZeroCount(presence);
                    Unsafe.Add(ref dst, i + j) = TF.ReadFixed(in Unsafe.Add(ref values, offset), le);
                    offset += size;
                    presence &= presence - 1;
                }
            }
            return;
        }

        // ビットマップはリーダーの内部領域を指すので、値を読む前に退避する
        byte[]? rented = null;
        Span<byte> copy = bitmapBytes <= 256 ? stackalloc byte[bitmapBytes] : (rented = ArrayPool<byte>.Shared.Rent(bitmapBytes)).AsSpan(0, bitmapBytes);
        MemoryMarshal.CreateReadOnlySpan(in bitmap, bitmapBytes).CopyTo(copy);
        for (int i = 0; i < count; i++)
        {
            destination[i] = (copy[i >> 3] & (1 << (i & 7))) != 0 ? TF.Read(ref reader, ref tracker, ref guard) : null;
        }
        if (rented is not null) ArrayPool<byte>.Shared.Return(rented);
    }
}

/// <summary>T?[](存在ビットマップ + 存在する値だけ)。</summary>
public readonly struct NullableArrayFormatter<T, TF> : IApexFormatter<T?[]?>
    where T : struct
    where TF : IApexFormatter<T>
{
    public static void Write<TWriter, TTracker>(ref TWriter writer, scoped in T?[]? value, ref TTracker tracker)
        where TWriter : struct, IApexBufferWriter, allows ref struct
        where TTracker : IReferenceTracker, allows ref struct
    {
        if (value is null) { writer.WriteNullCollectionHeader(); return; }
        NullableSequence.Write<TWriter, TTracker, T, TF>(ref writer, value, ref tracker);
    }

    public static T?[]? Read<TReader, TTracker, TGuard>(ref TReader reader, ref TTracker tracker, ref TGuard guard)
        where TReader : struct, IApexBufferReader, allows ref struct
        where TTracker : IReferenceTracker, allows ref struct
        where TGuard : ISecurityGuard, allows ref struct
    {
        int count = NullableSequence.ReadCount(ref reader, ref guard);
        if (count < 0) return null;
        if (count == 0) return Array.Empty<T?>();
        // 参照を含まない要素なら未初期化で確保してよい(ReadElements が全要素を書く)
        var array = RuntimeHelpers.IsReferenceOrContainsReferences<T>() ? new T?[count] : GC.AllocateUninitializedArray<T?>(count);
        NullableSequence.ReadElements<TReader, TTracker, TGuard, T, TF>(ref reader, array, ref tracker, ref guard);
        return array;
    }
}

/// <summary>List&lt;T?&gt;(存在ビットマップ + 存在する値だけ)。</summary>
public readonly struct NullableListFormatter<T, TF> : IApexFormatter<List<T?>?>
    where T : struct
    where TF : IApexFormatter<T>
{
    public static void Write<TWriter, TTracker>(ref TWriter writer, scoped in List<T?>? value, ref TTracker tracker)
        where TWriter : struct, IApexBufferWriter, allows ref struct
        where TTracker : IReferenceTracker, allows ref struct
    {
        if (value is null) { writer.WriteNullCollectionHeader(); return; }
        NullableSequence.Write<TWriter, TTracker, T, TF>(ref writer, CollectionsMarshal.AsSpan(value), ref tracker);
    }

    public static List<T?>? Read<TReader, TTracker, TGuard>(ref TReader reader, ref TTracker tracker, ref TGuard guard)
        where TReader : struct, IApexBufferReader, allows ref struct
        where TTracker : IReferenceTracker, allows ref struct
        where TGuard : ISecurityGuard, allows ref struct
    {
        int count = NullableSequence.ReadCount(ref reader, ref guard);
        if (count < 0) return null;
        var list = new List<T?>(count);
        CollectionsMarshal.SetCount(list, count);
        NullableSequence.ReadElements<TReader, TTracker, TGuard, T, TF>(ref reader, CollectionsMarshal.AsSpan(list), ref tracker, ref guard);
        return list;
    }
}

// ======================================================================
// 多次元配列: CompactUInt(d0 + 1), CompactUInt(d1), ... の後に行優先で要素
// ======================================================================

internal static class MultiDim
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Span<T> AsSpan<T>(Array array)
        => MemoryMarshal.CreateSpan(ref Unsafe.As<byte, T>(ref MemoryMarshal.GetArrayDataReference(array)), array.Length);

    public static void Write<TWriter, TTracker, T, TF>(ref TWriter writer, Array? value, ref TTracker tracker)
        where TWriter : struct, IApexBufferWriter, allows ref struct
        where TTracker : IReferenceTracker, allows ref struct
        where TF : IApexFormatter<T>
    {
        if (value is null) { writer.WriteNullCollectionHeader(); return; }
        writer.WriteCompactUInt((uint)value.GetLength(0) + 1);
        for (int d = 1; d < value.Rank; d++) writer.WriteCompactUInt((uint)value.GetLength(d));
        ApexSequence.WriteElements<TWriter, TTracker, T, TF>(ref writer, AsSpan<T>(value), ref tracker);
    }

    /// <summary>各次元の長さを読む。null なら false。</summary>
    public static bool ReadLengths<TReader, TGuard, T, TF>(ref TReader reader, ref TGuard guard, scoped Span<uint> lengths)
        where TReader : struct, IApexBufferReader, allows ref struct
        where TGuard : ISecurityGuard, allows ref struct
        where TF : IApexFormatter<T>
    {
        uint first = reader.ReadCompactUInt();
        if (first == 0) return false;
        lengths[0] = first - 1;
        for (int d = 1; d < lengths.Length; d++) lengths[d] = reader.ReadCompactUInt();
        foreach (uint length in lengths)
        {
            if (length > int.MaxValue) ApexThrowHelper.ThrowInvalidLength(int.MaxValue);
        }
        DictionaryHelper.CheckTotal(ref reader, ref guard, lengths, TF.MinWireSize);
        return true;
    }
}

public readonly struct Array2DFormatter<T, TF> : IApexFormatter<T[,]?> where TF : IApexFormatter<T>
{
    public static void Write<TWriter, TTracker>(ref TWriter writer, scoped in T[,]? value, ref TTracker tracker)
        where TWriter : struct, IApexBufferWriter, allows ref struct
        where TTracker : IReferenceTracker, allows ref struct
        => MultiDim.Write<TWriter, TTracker, T, TF>(ref writer, value, ref tracker);

    public static T[,]? Read<TReader, TTracker, TGuard>(ref TReader reader, ref TTracker tracker, ref TGuard guard)
        where TReader : struct, IApexBufferReader, allows ref struct
        where TTracker : IReferenceTracker, allows ref struct
        where TGuard : ISecurityGuard, allows ref struct
    {
        Span<uint> l = stackalloc uint[2];
        if (!MultiDim.ReadLengths<TReader, TGuard, T, TF>(ref reader, ref guard, l)) return null;
        var array = new T[(int)l[0], (int)l[1]];
        ApexSequence.ReadElements<TReader, TTracker, TGuard, T, TF>(ref reader, MultiDim.AsSpan<T>(array), ref tracker, ref guard);
        return array;
    }
}

public readonly struct Array3DFormatter<T, TF> : IApexFormatter<T[,,]?> where TF : IApexFormatter<T>
{
    public static void Write<TWriter, TTracker>(ref TWriter writer, scoped in T[,,]? value, ref TTracker tracker)
        where TWriter : struct, IApexBufferWriter, allows ref struct
        where TTracker : IReferenceTracker, allows ref struct
        => MultiDim.Write<TWriter, TTracker, T, TF>(ref writer, value, ref tracker);

    public static T[,,]? Read<TReader, TTracker, TGuard>(ref TReader reader, ref TTracker tracker, ref TGuard guard)
        where TReader : struct, IApexBufferReader, allows ref struct
        where TTracker : IReferenceTracker, allows ref struct
        where TGuard : ISecurityGuard, allows ref struct
    {
        Span<uint> l = stackalloc uint[3];
        if (!MultiDim.ReadLengths<TReader, TGuard, T, TF>(ref reader, ref guard, l)) return null;
        var array = new T[(int)l[0], (int)l[1], (int)l[2]];
        ApexSequence.ReadElements<TReader, TTracker, TGuard, T, TF>(ref reader, MultiDim.AsSpan<T>(array), ref tracker, ref guard);
        return array;
    }
}

public readonly struct Array4DFormatter<T, TF> : IApexFormatter<T[,,,]?> where TF : IApexFormatter<T>
{
    public static void Write<TWriter, TTracker>(ref TWriter writer, scoped in T[,,,]? value, ref TTracker tracker)
        where TWriter : struct, IApexBufferWriter, allows ref struct
        where TTracker : IReferenceTracker, allows ref struct
        => MultiDim.Write<TWriter, TTracker, T, TF>(ref writer, value, ref tracker);

    public static T[,,,]? Read<TReader, TTracker, TGuard>(ref TReader reader, ref TTracker tracker, ref TGuard guard)
        where TReader : struct, IApexBufferReader, allows ref struct
        where TTracker : IReferenceTracker, allows ref struct
        where TGuard : ISecurityGuard, allows ref struct
    {
        Span<uint> l = stackalloc uint[4];
        if (!MultiDim.ReadLengths<TReader, TGuard, T, TF>(ref reader, ref guard, l)) return null;
        var array = new T[(int)l[0], (int)l[1], (int)l[2], (int)l[3]];
        ApexSequence.ReadElements<TReader, TTracker, TGuard, T, TF>(ref reader, MultiDim.AsSpan<T>(array), ref tracker, ref guard);
        return array;
    }
}

// ======================================================================
// ILookup / IGrouping
// ======================================================================

internal sealed class ApexGrouping<TKey, TElement> : IGrouping<TKey, TElement>
{
    private readonly TElement[] _elements;

    public ApexGrouping(TKey key, TElement[] elements)
    {
        Key = key;
        _elements = elements;
    }

    public TKey Key { get; }
    public IEnumerator<TElement> GetEnumerator() => ((IEnumerable<TElement>)_elements).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => _elements.GetEnumerator();
}

/// <summary>ILookup&lt;TKey, TValue&gt;(グループ数、各グループのキーと要素列)。</summary>
public readonly struct LookupFormatter<TKey, TValue, FK, FV> : IApexFormatter<ILookup<TKey, TValue>?>
    where FK : IApexFormatter<TKey>
    where FV : IApexFormatter<TValue>
{
    public static void Write<TWriter, TTracker>(ref TWriter writer, scoped in ILookup<TKey, TValue>? value, ref TTracker tracker)
        where TWriter : struct, IApexBufferWriter, allows ref struct
        where TTracker : IReferenceTracker, allows ref struct
    {
        if (value is null) { writer.WriteNullCollectionHeader(); return; }
        writer.WriteCollectionHeader(value.Count);
        foreach (var group in value)
        {
            TKey key = group.Key;
            FK.Write(ref writer, in key, ref tracker);
            ApexSequence.WriteEnumerable<TWriter, TTracker, TValue, FV>(ref writer, group, ref tracker);
        }
    }

    public static ILookup<TKey, TValue>? Read<TReader, TTracker, TGuard>(ref TReader reader, ref TTracker tracker, ref TGuard guard)
        where TReader : struct, IApexBufferReader, allows ref struct
        where TTracker : IReferenceTracker, allows ref struct
        where TGuard : ISecurityGuard, allows ref struct
    {
        int count = reader.ReadCollectionHeader(ref guard, FK.MinWireSize + 1);
        if (count < 0) return null;
        var pairs = new List<KeyValuePair<TKey, TValue>>();
        for (int i = 0; i < count; i++)
        {
            TKey key = FK.Read(ref reader, ref tracker, ref guard);
            var elements = ApexSequence.ReadArray<TReader, TTracker, TGuard, TValue, FV>(ref reader, ref tracker, ref guard) ?? Array.Empty<TValue>();
            foreach (var element in elements) pairs.Add(new KeyValuePair<TKey, TValue>(key, element));
        }
        return pairs.ToLookup(static p => p.Key, static p => p.Value);
    }
}

/// <summary>IGrouping&lt;TKey, TValue&gt;(キーと要素列)。</summary>
public readonly struct GroupingFormatter<TKey, TValue, FK, FV> : IApexFormatter<IGrouping<TKey, TValue>?>
    where FK : IApexFormatter<TKey>
    where FV : IApexFormatter<TValue>
{
    public static void Write<TWriter, TTracker>(ref TWriter writer, scoped in IGrouping<TKey, TValue>? value, ref TTracker tracker)
        where TWriter : struct, IApexBufferWriter, allows ref struct
        where TTracker : IReferenceTracker, allows ref struct
    {
        if (value is null) { writer.GetSpanRef(1) = 0; return; }
        writer.GetSpanRef(1) = 1;
        TKey key = value.Key;
        FK.Write(ref writer, in key, ref tracker);
        ApexSequence.WriteEnumerable<TWriter, TTracker, TValue, FV>(ref writer, value, ref tracker);
    }

    public static IGrouping<TKey, TValue>? Read<TReader, TTracker, TGuard>(ref TReader reader, ref TTracker tracker, ref TGuard guard)
        where TReader : struct, IApexBufferReader, allows ref struct
        where TTracker : IReferenceTracker, allows ref struct
        where TGuard : ISecurityGuard, allows ref struct
    {
        if (reader.GetSpanRef(1) == 0) return null;
        TKey key = FK.Read(ref reader, ref tracker, ref guard);
        var elements = ApexSequence.ReadArray<TReader, TTracker, TGuard, TValue, FV>(ref reader, ref tracker, ref guard) ?? Array.Empty<TValue>();
        return new ApexGrouping<TKey, TValue>(key, elements);
    }
}

// ======================================================================
// その他の BCL 型
// ======================================================================

/// <summary>Uri(OriginalString を文字列として)。</summary>
public readonly struct UriFormatter : IApexFormatter<Uri?>
{
    public static void Write<TWriter, TTracker>(ref TWriter writer, scoped in Uri? value, ref TTracker tracker)
        where TWriter : struct, IApexBufferWriter, allows ref struct
        where TTracker : IReferenceTracker, allows ref struct
        => ApexStringCodec.Write(ref writer, value?.OriginalString);

    public static Uri? Read<TReader, TTracker, TGuard>(ref TReader reader, ref TTracker tracker, ref TGuard guard)
        where TReader : struct, IApexBufferReader, allows ref struct
        where TTracker : IReferenceTracker, allows ref struct
        where TGuard : ISecurityGuard, allows ref struct
    {
        string? s = ApexStringCodec.Read(ref reader);
        return s is null ? null : new Uri(s, UriKind.RelativeOrAbsolute);
    }
}

/// <summary>Version(0 = null、それ以外は成分数 + 1 と各成分)。</summary>
public readonly struct VersionFormatter : IApexFormatter<Version?>
{
    public static void Write<TWriter, TTracker>(ref TWriter writer, scoped in Version? value, ref TTracker tracker)
        where TWriter : struct, IApexBufferWriter, allows ref struct
        where TTracker : IReferenceTracker, allows ref struct
    {
        if (value is null) { writer.GetSpanRef(1) = 0; return; }
        int parts = value.Revision >= 0 ? 4 : value.Build >= 0 ? 3 : 2;
        writer.GetSpanRef(1) = (byte)(parts + 1);
        writer.WriteCompactUInt((uint)value.Major);
        writer.WriteCompactUInt((uint)value.Minor);
        if (parts >= 3) writer.WriteCompactUInt((uint)value.Build);
        if (parts >= 4) writer.WriteCompactUInt((uint)value.Revision);
    }

    public static Version? Read<TReader, TTracker, TGuard>(ref TReader reader, ref TTracker tracker, ref TGuard guard)
        where TReader : struct, IApexBufferReader, allows ref struct
        where TTracker : IReferenceTracker, allows ref struct
        where TGuard : ISecurityGuard, allows ref struct
    {
        int parts = reader.GetSpanRef(1) - 1;
        if (parts < 0) return null;
        int major = checked((int)reader.ReadCompactUInt());
        int minor = checked((int)reader.ReadCompactUInt());
        if (parts == 2) return new Version(major, minor);
        int build = checked((int)reader.ReadCompactUInt());
        if (parts == 3) return new Version(major, minor, build);
        return new Version(major, minor, build, checked((int)reader.ReadCompactUInt()));
    }
}

/// <summary>BigInteger(バイト数 + 2 の補数 LE)。</summary>
public readonly struct BigIntegerFormatter : IApexFormatter<BigInteger>
{
    public static void Write<TWriter, TTracker>(ref TWriter writer, scoped in BigInteger value, ref TTracker tracker)
        where TWriter : struct, IApexBufferWriter, allows ref struct
        where TTracker : IReferenceTracker, allows ref struct
    {
        int count = value.GetByteCount();
        writer.WriteCompactUInt((uint)count);
        var target = MemoryMarshal.CreateSpan(ref writer.GetSpanRef(count), count);
        value.TryWriteBytes(target, out _, isUnsigned: false, isBigEndian: false);
    }

    public static BigInteger Read<TReader, TTracker, TGuard>(ref TReader reader, ref TTracker tracker, ref TGuard guard)
        where TReader : struct, IApexBufferReader, allows ref struct
        where TTracker : IReferenceTracker, allows ref struct
        where TGuard : ISecurityGuard, allows ref struct
    {
        uint count = reader.ReadCompactUInt();
        if (count > (uint)reader.Remaining) ApexThrowHelper.ThrowInvalidLength((int)Math.Min(count, int.MaxValue));
        return new BigInteger(reader.ReadRaw((int)count), isUnsigned: false, isBigEndian: false);
    }
}

/// <summary>StringBuilder(文字列と同じ符号化)。</summary>
public readonly struct StringBuilderFormatter : IApexFormatter<StringBuilder?>
{
    public static void Write<TWriter, TTracker>(ref TWriter writer, scoped in StringBuilder? value, ref TTracker tracker)
        where TWriter : struct, IApexBufferWriter, allows ref struct
        where TTracker : IReferenceTracker, allows ref struct
    {
        if (value is null) { writer.GetSpanRef(1) = 0; return; }
        int length = value.Length;
        char[] buffer = ArrayPool<char>.Shared.Rent(Math.Max(length, 1));
        try
        {
            value.CopyTo(0, buffer, 0, length);
            ApexStringCodec.WriteChars(ref writer, buffer.AsSpan(0, length));
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buffer);
        }
    }

    public static StringBuilder? Read<TReader, TTracker, TGuard>(ref TReader reader, ref TTracker tracker, ref TGuard guard)
        where TReader : struct, IApexBufferReader, allows ref struct
        where TTracker : IReferenceTracker, allows ref struct
        where TGuard : ISecurityGuard, allows ref struct
    {
        string? s = ApexStringCodec.Read(ref reader);
        return s is null ? null : new StringBuilder(s);
    }
}

/// <summary>
/// Type(アセンブリ修飾名)。解決は防御器(呼び出し単位)に任せ、名前から型へのキャッシュは持たない。
/// </summary>
public readonly struct TypeFormatter : IApexFormatter<Type?>
{
    public static void Write<TWriter, TTracker>(ref TWriter writer, scoped in Type? value, ref TTracker tracker)
        where TWriter : struct, IApexBufferWriter, allows ref struct
        where TTracker : IReferenceTracker, allows ref struct
        => ApexStringCodec.Write(ref writer, value?.AssemblyQualifiedName);

    public static Type? Read<TReader, TTracker, TGuard>(ref TReader reader, ref TTracker tracker, ref TGuard guard)
        where TReader : struct, IApexBufferReader, allows ref struct
        where TTracker : IReferenceTracker, allows ref struct
        where TGuard : ISecurityGuard, allows ref struct
    {
        string? name = ApexStringCodec.Read(ref reader);
        if (name is null) return null;
        var type = guard.ResolveType(name);
        if (type is null) ApexThrowHelper.ThrowTypeNotResolved(name);
        return type;
    }
}

/// <summary>CultureInfo(カルチャ名)。</summary>
public readonly struct CultureInfoFormatter : IApexFormatter<CultureInfo?>
{
    public static void Write<TWriter, TTracker>(ref TWriter writer, scoped in CultureInfo? value, ref TTracker tracker)
        where TWriter : struct, IApexBufferWriter, allows ref struct
        where TTracker : IReferenceTracker, allows ref struct
        => ApexStringCodec.Write(ref writer, value?.Name);

    public static CultureInfo? Read<TReader, TTracker, TGuard>(ref TReader reader, ref TTracker tracker, ref TGuard guard)
        where TReader : struct, IApexBufferReader, allows ref struct
        where TTracker : IReferenceTracker, allows ref struct
        where TGuard : ISecurityGuard, allows ref struct
    {
        string? name = ApexStringCodec.Read(ref reader);
        return name is null ? null : CultureInfo.GetCultureInfo(name);
    }
}

/// <summary>TimeZoneInfo(ToSerializedString 形式)。</summary>
public readonly struct TimeZoneInfoFormatter : IApexFormatter<TimeZoneInfo?>
{
    public static void Write<TWriter, TTracker>(ref TWriter writer, scoped in TimeZoneInfo? value, ref TTracker tracker)
        where TWriter : struct, IApexBufferWriter, allows ref struct
        where TTracker : IReferenceTracker, allows ref struct
        => ApexStringCodec.Write(ref writer, value?.ToSerializedString());

    public static TimeZoneInfo? Read<TReader, TTracker, TGuard>(ref TReader reader, ref TTracker tracker, ref TGuard guard)
        where TReader : struct, IApexBufferReader, allows ref struct
        where TTracker : IReferenceTracker, allows ref struct
        where TGuard : ISecurityGuard, allows ref struct
    {
        string? s = ApexStringCodec.Read(ref reader);
        return s is null ? null : TimeZoneInfo.FromSerializedString(s);
    }
}

/// <summary>BitArray(ビット数 + 1 のヘッダーと詰めたビット列)。</summary>
public readonly struct BitArrayFormatter : IApexFormatter<BitArray?>
{
    public static void Write<TWriter, TTracker>(ref TWriter writer, scoped in BitArray? value, ref TTracker tracker)
        where TWriter : struct, IApexBufferWriter, allows ref struct
        where TTracker : IReferenceTracker, allows ref struct
    {
        if (value is null) { writer.WriteNullCollectionHeader(); return; }
        int length = value.Length;
        writer.WriteCollectionHeader(length);
        int bytes = (length + 7) >> 3;
        if (bytes == 0) return;
        var buffer = new byte[bytes];
        value.CopyTo(buffer, 0);
        writer.WriteRaw(buffer);
    }

    public static BitArray? Read<TReader, TTracker, TGuard>(ref TReader reader, ref TTracker tracker, ref TGuard guard)
        where TReader : struct, IApexBufferReader, allows ref struct
        where TTracker : IReferenceTracker, allows ref struct
        where TGuard : ISecurityGuard, allows ref struct
    {
        uint header = reader.ReadCompactUInt();
        if (header == 0) return null;
        uint length = header - 1;
        int bytes = (int)((length + 7) >> 3);
        if (length > int.MaxValue || bytes > reader.Remaining) ApexThrowHelper.ThrowInvalidLength((int)Math.Min(length, int.MaxValue));
        return new BitArray(reader.ReadRaw(bytes).ToArray()) { Length = (int)length };
    }
}
