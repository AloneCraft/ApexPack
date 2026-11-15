using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ApexPack.Buffers;

/// <summary>
/// writer / reader 共通の符号化操作。各 writer/reader 型に特殊化されて JIT でインライン展開される。
/// </summary>
public static class ApexBufferExtensions
{
    // ================= writer =================

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteByte<TWriter>(ref this TWriter writer, byte value)
        where TWriter : struct, IApexBufferWriter, allows ref struct
        => writer.GetSpanRef(1) = value;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteSByte<TWriter>(ref this TWriter writer, sbyte value)
        where TWriter : struct, IApexBufferWriter, allows ref struct
        => writer.GetSpanRef(1) = (byte)value;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteBoolean<TWriter>(ref this TWriter writer, bool value)
        where TWriter : struct, IApexBufferWriter, allows ref struct
        => writer.GetSpanRef(1) = value ? (byte)1 : (byte)0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteChar<TWriter>(ref this TWriter writer, char value)
        where TWriter : struct, IApexBufferWriter, allows ref struct
        => ApexWire.WriteUInt16(ref writer.GetSpanRef(2), value, TWriter.IsHostLittleEndian);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteInt16<TWriter>(ref this TWriter writer, short value)
        where TWriter : struct, IApexBufferWriter, allows ref struct
        => ApexWire.WriteUInt16(ref writer.GetSpanRef(2), (ushort)value, TWriter.IsHostLittleEndian);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteUInt16<TWriter>(ref this TWriter writer, ushort value)
        where TWriter : struct, IApexBufferWriter, allows ref struct
        => ApexWire.WriteUInt16(ref writer.GetSpanRef(2), value, TWriter.IsHostLittleEndian);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteInt32<TWriter>(ref this TWriter writer, int value)
        where TWriter : struct, IApexBufferWriter, allows ref struct
        => ApexWire.WriteUInt32(ref writer.GetSpanRef(4), (uint)value, TWriter.IsHostLittleEndian);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteUInt32<TWriter>(ref this TWriter writer, uint value)
        where TWriter : struct, IApexBufferWriter, allows ref struct
        => ApexWire.WriteUInt32(ref writer.GetSpanRef(4), value, TWriter.IsHostLittleEndian);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteInt64<TWriter>(ref this TWriter writer, long value)
        where TWriter : struct, IApexBufferWriter, allows ref struct
        => ApexWire.WriteUInt64(ref writer.GetSpanRef(8), (ulong)value, TWriter.IsHostLittleEndian);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteUInt64<TWriter>(ref this TWriter writer, ulong value)
        where TWriter : struct, IApexBufferWriter, allows ref struct
        => ApexWire.WriteUInt64(ref writer.GetSpanRef(8), value, TWriter.IsHostLittleEndian);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteSingle<TWriter>(ref this TWriter writer, float value)
        where TWriter : struct, IApexBufferWriter, allows ref struct
        => ApexWire.WriteUInt32(ref writer.GetSpanRef(4), BitConverter.SingleToUInt32Bits(value), TWriter.IsHostLittleEndian);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteDouble<TWriter>(ref this TWriter writer, double value)
        where TWriter : struct, IApexBufferWriter, allows ref struct
        => ApexWire.WriteUInt64(ref writer.GetSpanRef(8), BitConverter.DoubleToUInt64Bits(value), TWriter.IsHostLittleEndian);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteDecimal<TWriter>(ref this TWriter writer, decimal value)
        where TWriter : struct, IApexBufferWriter, allows ref struct
        => ApexWire.Write(ref writer.GetSpanRef(16), value, TWriter.IsHostLittleEndian);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteGuid<TWriter>(ref this TWriter writer, Guid value)
        where TWriter : struct, IApexBufferWriter, allows ref struct
        => ApexWire.Write(ref writer.GetSpanRef(16), value, TWriter.IsHostLittleEndian);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteDateTime<TWriter>(ref this TWriter writer, DateTime value)
        where TWriter : struct, IApexBufferWriter, allows ref struct
        => ApexWire.Write(ref writer.GetSpanRef(8), value, TWriter.IsHostLittleEndian);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteTimeSpan<TWriter>(ref this TWriter writer, TimeSpan value)
        where TWriter : struct, IApexBufferWriter, allows ref struct
        => ApexWire.Write(ref writer.GetSpanRef(8), value, TWriter.IsHostLittleEndian);

    /// <summary>既知のスカラー型(プリミティブ、enum、日時、Guid、decimal、数値ベクトルなど)を正規形で書く。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteUnmanaged<TWriter, T>(ref this TWriter writer, T value)
        where TWriter : struct, IApexBufferWriter, allows ref struct
        where T : unmanaged
        => ApexWire.Write(ref writer.GetSpanRef(ApexWire.SizeOf<T>()), value, TWriter.IsHostLittleEndian);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteRaw<TWriter>(ref this TWriter writer, scoped ReadOnlySpan<byte> source)
        where TWriter : struct, IApexBufferWriter, allows ref struct
    {
        if (source.IsEmpty) return;
        Unsafe.CopyBlockUnaligned(ref writer.GetSpanRef(source.Length), ref MemoryMarshal.GetReference(source), (uint)source.Length);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteCompactUInt<TWriter>(ref this TWriter writer, uint value)
        where TWriter : struct, IApexBufferWriter, allows ref struct
    {
        if (value <= ApexWire.CompactOneByteMax)
        {
            writer.GetSpanRef(1) = (byte)value;
            return;
        }
        int n = ApexWire.WriteCompact(ref writer.Reserve(ApexWire.CompactMaxSize), value, TWriter.IsHostLittleEndian);
        writer.Advance(n);
    }

    /// <summary>コレクションの件数ヘッダー(<c>count + 1</c>)を書く。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteCollectionHeader<TWriter>(ref this TWriter writer, int count)
        where TWriter : struct, IApexBufferWriter, allows ref struct
        => writer.WriteCompactUInt((uint)count + 1);

    /// <summary>null コレクションを表すヘッダー(0)を書く。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteNullCollectionHeader<TWriter>(ref this TWriter writer)
        where TWriter : struct, IApexBufferWriter, allows ref struct
        => writer.GetSpanRef(1) = 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteString<TWriter>(ref this TWriter writer, string? value)
        where TWriter : struct, IApexBufferWriter, allows ref struct
        => ApexStringCodec.Write(ref writer, value);

    // ================= reader =================

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte ReadByte<TReader>(ref this TReader reader)
        where TReader : struct, IApexBufferReader, allows ref struct
        => reader.GetSpanRef(1);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static sbyte ReadSByte<TReader>(ref this TReader reader)
        where TReader : struct, IApexBufferReader, allows ref struct
        => (sbyte)reader.GetSpanRef(1);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool ReadBoolean<TReader>(ref this TReader reader)
        where TReader : struct, IApexBufferReader, allows ref struct
        => reader.GetSpanRef(1) != 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static char ReadChar<TReader>(ref this TReader reader)
        where TReader : struct, IApexBufferReader, allows ref struct
        => (char)ApexWire.ReadUInt16(in reader.GetSpanRef(2), TReader.IsHostLittleEndian);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static short ReadInt16<TReader>(ref this TReader reader)
        where TReader : struct, IApexBufferReader, allows ref struct
        => (short)ApexWire.ReadUInt16(in reader.GetSpanRef(2), TReader.IsHostLittleEndian);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ushort ReadUInt16<TReader>(ref this TReader reader)
        where TReader : struct, IApexBufferReader, allows ref struct
        => ApexWire.ReadUInt16(in reader.GetSpanRef(2), TReader.IsHostLittleEndian);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ReadInt32<TReader>(ref this TReader reader)
        where TReader : struct, IApexBufferReader, allows ref struct
        => (int)ApexWire.ReadUInt32(in reader.GetSpanRef(4), TReader.IsHostLittleEndian);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint ReadUInt32<TReader>(ref this TReader reader)
        where TReader : struct, IApexBufferReader, allows ref struct
        => ApexWire.ReadUInt32(in reader.GetSpanRef(4), TReader.IsHostLittleEndian);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long ReadInt64<TReader>(ref this TReader reader)
        where TReader : struct, IApexBufferReader, allows ref struct
        => (long)ApexWire.ReadUInt64(in reader.GetSpanRef(8), TReader.IsHostLittleEndian);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong ReadUInt64<TReader>(ref this TReader reader)
        where TReader : struct, IApexBufferReader, allows ref struct
        => ApexWire.ReadUInt64(in reader.GetSpanRef(8), TReader.IsHostLittleEndian);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float ReadSingle<TReader>(ref this TReader reader)
        where TReader : struct, IApexBufferReader, allows ref struct
        => BitConverter.UInt32BitsToSingle(ApexWire.ReadUInt32(in reader.GetSpanRef(4), TReader.IsHostLittleEndian));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double ReadDouble<TReader>(ref this TReader reader)
        where TReader : struct, IApexBufferReader, allows ref struct
        => BitConverter.UInt64BitsToDouble(ApexWire.ReadUInt64(in reader.GetSpanRef(8), TReader.IsHostLittleEndian));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static decimal ReadDecimal<TReader>(ref this TReader reader)
        where TReader : struct, IApexBufferReader, allows ref struct
        => ApexWire.Read<decimal>(in reader.GetSpanRef(16), TReader.IsHostLittleEndian);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Guid ReadGuid<TReader>(ref this TReader reader)
        where TReader : struct, IApexBufferReader, allows ref struct
        => ApexWire.Read<Guid>(in reader.GetSpanRef(16), TReader.IsHostLittleEndian);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DateTime ReadDateTime<TReader>(ref this TReader reader)
        where TReader : struct, IApexBufferReader, allows ref struct
        => ApexWire.Read<DateTime>(in reader.GetSpanRef(8), TReader.IsHostLittleEndian);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TimeSpan ReadTimeSpan<TReader>(ref this TReader reader)
        where TReader : struct, IApexBufferReader, allows ref struct
        => ApexWire.Read<TimeSpan>(in reader.GetSpanRef(8), TReader.IsHostLittleEndian);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T ReadUnmanaged<TReader, T>(ref this TReader reader)
        where TReader : struct, IApexBufferReader, allows ref struct
        where T : unmanaged
        => ApexWire.Read<T>(in reader.GetSpanRef(ApexWire.SizeOf<T>()), TReader.IsHostLittleEndian);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ReadOnlySpan<byte> ReadRaw<TReader>(ref this TReader reader, int length)
        where TReader : struct, IApexBufferReader, allows ref struct
    {
        if (length == 0) return default;
        return MemoryMarshal.CreateReadOnlySpan(in reader.GetSpanRef(length), length);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint ReadCompactUInt<TReader>(ref this TReader reader)
        where TReader : struct, IApexBufferReader, allows ref struct
    {
        byte first = reader.GetSpanRef(1);
        if (first <= ApexWire.CompactOneByteMax) return first;
        return ReadCompactUIntSlow(ref reader, first);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static uint ReadCompactUIntSlow<TReader>(ref TReader reader, byte first)
        where TReader : struct, IApexBufferReader, allows ref struct
    {
        if (first == 0xFD) return ApexWire.ReadUInt16(in reader.GetSpanRef(2), TReader.IsHostLittleEndian);
        if (first == 0xFE) return ApexWire.ReadUInt32(in reader.GetSpanRef(4), TReader.IsHostLittleEndian);
        ApexThrowHelper.ThrowInvalidCompactHeader(first);
        return 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string? ReadString<TReader>(ref this TReader reader)
        where TReader : struct, IApexBufferReader, allows ref struct
        => ApexStringCodec.Read(ref reader);

    /// <summary>
    /// コレクションの件数ヘッダーを読む。null なら -1。
    /// 残りバイト数に収まらない件数(1 要素 <paramref name="minElementSize"/> バイト以上として)は割り当て前に拒否する。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ReadCollectionHeader<TReader, TGuard>(ref this TReader reader, ref TGuard guard, int minElementSize = 1)
        where TReader : struct, IApexBufferReader, allows ref struct
        where TGuard : ISecurityGuard, allows ref struct
    {
        uint header = reader.ReadCompactUInt();
        if (header == 0) return -1;
        uint count = header - 1;
        if ((ulong)count * (uint)minElementSize > (uint)reader.Remaining || count > int.MaxValue)
        {
            ApexThrowHelper.ThrowInvalidLength((int)Math.Min(count, int.MaxValue));
        }
        guard.AddElements((int)count);
        return (int)count;
    }
}
