using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ApexPack.Buffers;

/// <summary>
/// High-performance ref struct writer operating directly on a fixed Span&lt;byte&gt;.
/// Completely zero heap allocation.
/// </summary>
public ref struct ApexSpanWriter : IApexBufferWriter
{
    private Span<byte> _span;
    private int _written;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ApexSpanWriter(Span<byte> destination)
    {
        _span = destination;
        _written = 0;
    }

    public readonly int BytesWritten => _written;
    public readonly ReadOnlySpan<byte> WrittenSpan => _span.Slice(0, _written);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Span<byte> GetSpan(int size)
    {
        if ((uint)(_written + size) > (uint)_span.Length)
        {
            ThrowBufferTooSmall();
        }
        var target = _span.Slice(_written, size);
        _written += size;
        return target;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowBufferTooSmall()
    {
        throw new InvalidOperationException("Destination buffer is too small to complete the serialization operation.");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteByte(byte value) => GetSpan(1)[0] = value;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteSByte(sbyte value) => GetSpan(1)[0] = (byte)value;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteChar(char value) => Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(GetSpan(sizeof(char))), value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteInt16(short value) => Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(GetSpan(sizeof(short))), value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteUInt16(ushort value) => Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(GetSpan(sizeof(ushort))), value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteInt32(int value) => Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(GetSpan(sizeof(int))), value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteUInt32(uint value) => Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(GetSpan(sizeof(uint))), value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteInt64(long value) => Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(GetSpan(sizeof(long))), value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteUInt64(ulong value) => Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(GetSpan(sizeof(ulong))), value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteSingle(float value) => Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(GetSpan(sizeof(float))), value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteDouble(double value) => Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(GetSpan(sizeof(double))), value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteBoolean(bool value) => WriteByte(value ? (byte)1 : (byte)0);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteGuid(Guid value) => Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(GetSpan(Unsafe.SizeOf<Guid>())), value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteDateTime(DateTime value) => WriteInt64(value.ToBinary());

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteTimeSpan(TimeSpan value) => WriteInt64(value.Ticks);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteDecimal(decimal value) => Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(GetSpan(Unsafe.SizeOf<decimal>())), value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteRaw(ReadOnlySpan<byte> source)
    {
        if (source.IsEmpty) return;
        source.CopyTo(GetSpan(source.Length));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteBitmask8(byte mask) => WriteByte(mask);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteBitmask16(ushort mask) => WriteUInt16(mask);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteBitmask32(uint mask) => WriteUInt32(mask);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteBitmask64(ulong mask) => WriteUInt64(mask);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteUnmanaged<T>(in T value) where T : unmanaged
    {
        Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(GetSpan(Unsafe.SizeOf<T>())), value);
    }

    /// <summary>
    /// Writes a System.String directly as UTF-16 in-memory memory blit (zero transcoding overhead).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteString(string? value)
    {
        if (value is null)
        {
            WriteInt32(-1);
            return;
        }

        int charLength = value.Length;
        WriteInt32(charLength);
        if (charLength > 0)
        {
            ReadOnlySpan<byte> byteSpan = MemoryMarshal.AsBytes(value.AsSpan());
            WriteRaw(byteSpan);
        }
    }
}
