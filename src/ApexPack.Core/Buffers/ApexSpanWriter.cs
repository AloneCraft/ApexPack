using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using ApexPack.Hardware;

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
    public void WriteByte(byte value)
    {
        GetSpan(1)[0] = value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteInt16(short value)
    {
        Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(GetSpan(sizeof(short))), value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteUInt16(ushort value)
    {
        Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(GetSpan(sizeof(ushort))), value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteInt32(int value)
    {
        Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(GetSpan(sizeof(int))), value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteUInt32(uint value)
    {
        Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(GetSpan(sizeof(uint))), value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteInt64(long value)
    {
        Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(GetSpan(sizeof(long))), value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteUInt64(ulong value)
    {
        Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(GetSpan(sizeof(ulong))), value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteSingle(float value)
    {
        Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(GetSpan(sizeof(float))), value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteDouble(double value)
    {
        Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(GetSpan(sizeof(double))), value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteBoolean(bool value)
    {
        WriteByte(value ? (byte)1 : (byte)0);
    }

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

    public void WriteString(string? value)
    {
        if (value is null)
        {
            WriteInt32(-1);
            return;
        }

        if (value.Length == 0)
        {
            WriteInt32(0);
            return;
        }

        var chars = value.AsSpan();
        if (HardwareIntrinsics.IsAllAscii(chars))
        {
            // ASCII format flag: positive byte length with bit 31 unset
            int byteCount = chars.Length;
            WriteInt32(byteCount);
            var dest = GetSpan(byteCount);
            HardwareIntrinsics.CopyAsciiCharsToUtf8(chars, dest);
        }
        else
        {
            int maxBytes = Encoding.UTF8.GetByteCount(chars);
            // Non-ASCII flag: negative length encoding or distinct length
            // We write actual UTF-8 byte count
            WriteInt32(maxBytes);
            var dest = GetSpan(maxBytes);
            Encoding.UTF8.GetBytes(chars, dest);
        }
    }
}
