using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ApexPack.Buffers;

/// <summary>
/// <see cref="ReadOnlySpan{T}"/> から読む reader。範囲外は <see cref="InvalidOperationException"/>。
/// </summary>
public ref struct ApexSpanReader : IApexBufferReader
{
    private readonly ReadOnlySpan<byte> _span;
    private int _consumed;

    public static bool IsHostLittleEndian => BitConverter.IsLittleEndian;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ApexSpanReader(ReadOnlySpan<byte> source)
    {
        _span = source;
        _consumed = 0;
    }

    public readonly int Consumed => _consumed;
    public readonly int Remaining => _span.Length - _consumed;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref readonly byte GetSpanRef(int size)
    {
        if ((uint)size > (uint)(_span.Length - _consumed))
        {
            ThrowUnexpectedEndOfBuffer();
        }
        ref readonly byte p = ref Unsafe.Add(ref MemoryMarshal.GetReference(_span), _consumed);
        _consumed += size;
        return ref p;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly byte PeekByte()
    {
        if ((uint)_consumed >= (uint)_span.Length)
        {
            ThrowUnexpectedEndOfBuffer();
        }
        return Unsafe.Add(ref MemoryMarshal.GetReference(_span), _consumed);
    }

    /// <summary>まだ読んでいない残りの領域。</summary>
    public readonly ReadOnlySpan<byte> RemainingSpan => _span.Slice(_consumed);

    /// <summary>
    /// 読み進めずに、現在位置の参照を返す。<paramref name="sizeHint"/> バイトが残っていることを確かめる。
    /// 読んだ後は <see cref="Advance"/> で位置を進める。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly ref readonly byte GetSpanReference(int sizeHint)
    {
        if ((uint)sizeHint > (uint)(_span.Length - _consumed))
        {
            ThrowUnexpectedEndOfBuffer();
        }
        return ref Unsafe.Add(ref MemoryMarshal.GetReference(_span), _consumed);
    }

    /// <summary>読み取り位置を進める。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Advance(int count)
    {
        if ((uint)count > (uint)(_span.Length - _consumed))
        {
            ThrowUnexpectedEndOfBuffer();
        }
        _consumed += count;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void ThrowUnexpectedEndOfBuffer()
    {
        throw new InvalidOperationException("Unexpected end of buffer while reading.");
    }
}

/// <summary>
/// <see cref="ApexBigEndianSimWriter"/> の出力を読む reader(反転経路の検証用)。
/// </summary>
public ref struct ApexBigEndianSimReader : IApexBufferReader
{
    private ApexSpanReader _inner;

    public static bool IsHostLittleEndian => false;

    public ApexBigEndianSimReader(ReadOnlySpan<byte> source) => _inner = new ApexSpanReader(source);

    public readonly int Consumed => _inner.Consumed;
    public readonly int Remaining => _inner.Remaining;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref readonly byte GetSpanRef(int size) => ref _inner.GetSpanRef(size);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly byte PeekByte() => _inner.PeekByte();
}
