using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ApexPack.Buffers;

/// <summary>
/// High-performance ref struct reader operating directly on a ReadOnlySpan&lt;byte&gt;.
/// Completely zero heap allocation.
/// </summary>
public ref struct ApexSpanReader : IApexBufferReader
{
    private readonly ReadOnlySpan<byte> _span;
    private int _consumed;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ApexSpanReader(ReadOnlySpan<byte> source)
    {
        _span = source;
        _consumed = 0;
    }

    public readonly int Consumed => _consumed;
    public readonly int Remaining => _span.Length - _consumed;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ReadOnlySpan<byte> GetSpan(int size)
    {
        if ((uint)(_consumed + size) > (uint)_span.Length)
        {
            ThrowUnexpectedEndOfBuffer();
        }
        var slice = _span.Slice(_consumed, size);
        _consumed += size;
        return slice;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowUnexpectedEndOfBuffer()
    {
        throw new InvalidOperationException("Unexpected end of buffer while reading.");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte ReadByte() => GetSpan(1)[0];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public sbyte ReadSByte() => (sbyte)GetSpan(1)[0];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public char ReadChar() => Unsafe.ReadUnaligned<char>(ref MemoryMarshal.GetReference(GetSpan(sizeof(char))));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public short ReadInt16() => Unsafe.ReadUnaligned<short>(ref MemoryMarshal.GetReference(GetSpan(sizeof(short))));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ushort ReadUInt16() => Unsafe.ReadUnaligned<ushort>(ref MemoryMarshal.GetReference(GetSpan(sizeof(ushort))));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int ReadInt32() => Unsafe.ReadUnaligned<int>(ref MemoryMarshal.GetReference(GetSpan(sizeof(int))));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint ReadUInt32() => Unsafe.ReadUnaligned<uint>(ref MemoryMarshal.GetReference(GetSpan(sizeof(uint))));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public long ReadInt64() => Unsafe.ReadUnaligned<long>(ref MemoryMarshal.GetReference(GetSpan(sizeof(long))));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ulong ReadUInt64() => Unsafe.ReadUnaligned<ulong>(ref MemoryMarshal.GetReference(GetSpan(sizeof(ulong))));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float ReadSingle() => Unsafe.ReadUnaligned<float>(ref MemoryMarshal.GetReference(GetSpan(sizeof(float))));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double ReadDouble() => Unsafe.ReadUnaligned<double>(ref MemoryMarshal.GetReference(GetSpan(sizeof(double))));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ReadBoolean() => ReadByte() != 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Guid ReadGuid() => Unsafe.ReadUnaligned<Guid>(ref MemoryMarshal.GetReference(GetSpan(Unsafe.SizeOf<Guid>())));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public DateTime ReadDateTime() => DateTime.FromBinary(ReadInt64());

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TimeSpan ReadTimeSpan() => TimeSpan.FromTicks(ReadInt64());

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public decimal ReadDecimal() => Unsafe.ReadUnaligned<decimal>(ref MemoryMarshal.GetReference(GetSpan(Unsafe.SizeOf<decimal>())));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySpan<byte> ReadRaw(int length) => GetSpan(length);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte ReadBitmask8() => ReadByte();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ushort ReadBitmask16() => ReadUInt16();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint ReadBitmask32() => ReadUInt32();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ulong ReadBitmask64() => ReadUInt64();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T ReadUnmanaged<T>() where T : unmanaged
    {
        return Unsafe.ReadUnaligned<T>(ref MemoryMarshal.GetReference(GetSpan(Unsafe.SizeOf<T>())));
    }

    /// <summary>
    /// Reads a System.String directly from UTF-16 in-memory memory blit (zero transcoding overhead).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public string? ReadString()
    {
        int charLength = ReadInt32();
        if (charLength == -1) return null;
        if (charLength == 0) return string.Empty;

        int byteLength = checked(charLength * sizeof(char));
        ReadOnlySpan<byte> byteSpan = GetSpan(byteLength);
        ReadOnlySpan<char> charSpan = MemoryMarshal.Cast<byte, char>(byteSpan);
        return new string(charSpan);
    }
}
