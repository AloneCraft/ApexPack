using System;
using System.Runtime.CompilerServices;

namespace ApexPack.Buffers;

/// <summary>
/// 手書きのフォーマッタ向けの低レベル操作(可変長整数、オブジェクトヘッダー、null の確認)。符号化は ApexPack 形式(docs/WireFormat.md)。
/// </summary>
public static class ApexLowLevelExtensions
{
    // ================= 可変長整数: 符号付きは ZigZag、続けて 7 ビットずつ下位から(最上位ビットが継続の印) =================

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong ZigZag(long value) => (ulong)((value << 1) ^ (value >> 63));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long UnZigZag(ulong value) => (long)(value >> 1) ^ -(long)(value & 1);

    /// <summary>符号なしの可変長整数を書く(1〜10 バイト)。</summary>
    public static void WriteVarUInt<TWriter>(ref this TWriter writer, ulong value)
        where TWriter : struct, IApexBufferWriter, allows ref struct
    {
        if (value < 0x80)
        {
            writer.GetSpanRef(1) = (byte)value;
            return;
        }
        ref byte p = ref writer.Reserve(10);
        int n = 0;
        while (value >= 0x80)
        {
            Unsafe.Add(ref p, n++) = (byte)(value | 0x80);
            value >>= 7;
        }
        Unsafe.Add(ref p, n++) = (byte)value;
        writer.Advance(n);
    }

    /// <summary>符号付きの可変長整数を書く(ZigZag で小さな負数も短くなる)。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteVarInt<TWriter>(ref this TWriter writer, long value)
        where TWriter : struct, IApexBufferWriter, allows ref struct
        => writer.WriteVarUInt(ZigZag(value));

    public static ulong ReadVarUInt<TReader>(ref this TReader reader)
        where TReader : struct, IApexBufferReader, allows ref struct
    {
        ulong result = 0;
        for (int shift = 0; shift < 70; shift += 7)
        {
            byte b = reader.GetSpanRef(1);
            if (shift == 63 && b > 1) break; // 64 ビットを超える
            result |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) return result;
        }
        throw new ApexSecurityException("Malformed variable-length integer.");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long ReadVarInt<TReader>(ref this TReader reader)
        where TReader : struct, IApexBufferReader, allows ref struct
        => UnZigZag(reader.ReadVarUInt());

    // ================= オブジェクトヘッダー: 1 バイト。0 = null、それ以外はメンバー数 + 1 =================

    /// <summary>オブジェクトのヘッダー(メンバー数 0〜254)を書く。</summary>
    public static void WriteObjectHeader<TWriter>(ref this TWriter writer, byte memberCount)
        where TWriter : struct, IApexBufferWriter, allows ref struct
    {
        if (memberCount == byte.MaxValue) throw new ArgumentOutOfRangeException(nameof(memberCount), "Member count must be 254 or less.");
        writer.GetSpanRef(1) = (byte)(memberCount + 1);
    }

    /// <summary>null のオブジェクトヘッダーを書く。</summary>
    public static void WriteNullObjectHeader<TWriter>(ref this TWriter writer)
        where TWriter : struct, IApexBufferWriter, allows ref struct
        => writer.GetSpanRef(1) = 0;

    /// <summary>オブジェクトのヘッダーを読む。null なら false。</summary>
    public static bool TryReadObjectHeader<TReader>(ref this TReader reader, out byte memberCount)
        where TReader : struct, IApexBufferReader, allows ref struct
    {
        byte header = reader.GetSpanRef(1);
        memberCount = header == 0 ? (byte)0 : (byte)(header - 1);
        return header != 0;
    }

    /// <summary>次のオブジェクトヘッダー・コレクションヘッダーが null なら true(読み進めない)。</summary>
    public static bool PeekIsNull<TReader>(ref this TReader reader)
        where TReader : struct, IApexBufferReader, allows ref struct
        => reader.PeekByte() == 0;

    /// <summary>コレクションの件数ヘッダーを読む。null なら false。</summary>
    public static bool TryReadCollectionHeader<TReader>(ref this TReader reader, out int length)
        where TReader : struct, IApexBufferReader, allows ref struct
    {
        var guard = new TrustedSecurityGuard();
        length = reader.ReadCollectionHeader(ref guard);
        if (length < 0)
        {
            length = 0;
            return false;
        }
        return true;
    }
}
