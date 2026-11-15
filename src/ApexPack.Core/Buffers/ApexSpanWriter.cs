using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ApexPack.Buffers;

/// <summary>
/// 固定長の <see cref="Span{T}"/> へ書き込む writer。容量不足は <see cref="InvalidOperationException"/>。
/// </summary>
public ref struct ApexSpanWriter : IApexBufferWriter
{
    private readonly Span<byte> _span;
    private int _written;

    public static bool IsHostLittleEndian => BitConverter.IsLittleEndian;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ApexSpanWriter(Span<byte> destination)
    {
        _span = destination;
        _written = 0;
    }

    public readonly int BytesWritten => _written;
    public readonly ReadOnlySpan<byte> WrittenSpan => _span.Slice(0, _written);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref byte GetSpanRef(int size)
    {
        ref byte p = ref Reserve(size);
        _written += size;
        return ref p;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref byte Reserve(int maxSize)
    {
        if ((uint)maxSize > (uint)(_span.Length - _written))
        {
            ThrowBufferTooSmall();
        }
        return ref Unsafe.Add(ref MemoryMarshal.GetReference(_span), _written);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Advance(int count) => _written += count;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowBufferTooSmall()
    {
        throw new InvalidOperationException("Destination buffer is too small to complete the serialization operation.");
    }
}

/// <summary>
/// ワイヤ形式の反転経路を LE 機で検証するための writer。ネイティブ表現を BE とみなすため、
/// 出力は正規形の各数値フィールドをバイト反転したものになる。<see cref="ApexBigEndianSimReader"/> で往復できる。
/// </summary>
public ref struct ApexBigEndianSimWriter : IApexBufferWriter
{
    private ApexSpanWriter _inner;

    public static bool IsHostLittleEndian => false;

    public ApexBigEndianSimWriter(Span<byte> destination) => _inner = new ApexSpanWriter(destination);

    public readonly int BytesWritten => _inner.BytesWritten;
    public readonly ReadOnlySpan<byte> WrittenSpan => _inner.WrittenSpan;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref byte GetSpanRef(int size) => ref _inner.GetSpanRef(size);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref byte Reserve(int maxSize) => ref _inner.Reserve(maxSize);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Advance(int count) => _inner.Advance(count);
}
