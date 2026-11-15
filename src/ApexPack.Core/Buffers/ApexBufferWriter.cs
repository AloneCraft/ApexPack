using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ApexPack.Buffers;

/// <summary>
/// 自動伸長する writer。初期バッファを外部から受け取れ(スレッドローカルの再利用)、
/// 不足したら <see cref="ArrayPool{T}"/> から倍々に借りる。借りた配列は <see cref="Dispose"/> で返す。
/// </summary>
public ref struct ApexBufferWriter : IApexBufferWriter, IDisposable
{
    private byte[] _buffer;
    private int _written;
    private bool _rented;

    public static bool IsHostLittleEndian => BitConverter.IsLittleEndian;

    public ApexBufferWriter(int initialCapacity = 256)
    {
        _buffer = ArrayPool<byte>.Shared.Rent(Math.Max(initialCapacity, 64));
        _written = 0;
        _rented = true;
    }

    /// <summary>呼び出し側が所有する初期バッファで開始する。この配列はプールへ返さない。</summary>
    public ApexBufferWriter(byte[] initialBuffer)
    {
        _buffer = initialBuffer;
        _written = 0;
        _rented = false;
    }

    public readonly int BytesWritten => _written;
    public readonly ReadOnlySpan<byte> WrittenSpan => _buffer.AsSpan(0, _written);
    public readonly ReadOnlyMemory<byte> WrittenMemory => _buffer.AsMemory(0, _written);

    public readonly byte[] ToArray()
    {
        if (_written == 0) return Array.Empty<byte>();
        var array = GC.AllocateUninitializedArray<byte>(_written);
        Unsafe.CopyBlockUnaligned(ref MemoryMarshal.GetArrayDataReference(array), ref MemoryMarshal.GetArrayDataReference(_buffer), (uint)_written);
        return array;
    }

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
        if ((uint)maxSize > (uint)(_buffer.Length - _written))
        {
            Grow(maxSize);
        }
        return ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(_buffer), _written);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Advance(int count) => _written += count;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void Grow(int size)
    {
        long required = (long)_written + (uint)size;
        if (size < 0 || required > Array.MaxLength)
        {
            throw new InvalidOperationException($"Serialized payload exceeds the maximum array length ({required} bytes).");
        }

        int newCapacity = (int)Math.Min(Math.Max((long)_buffer.Length * 2, required), Array.MaxLength);
        var newBuffer = ArrayPool<byte>.Shared.Rent(newCapacity);
        _buffer.AsSpan(0, _written).CopyTo(newBuffer);
        if (_rented)
        {
            ArrayPool<byte>.Shared.Return(_buffer);
        }
        _buffer = newBuffer;
        _rented = true;
    }

    public void Dispose()
    {
        if (_rented && _buffer is not null)
        {
            ArrayPool<byte>.Shared.Return(_buffer);
        }
        _buffer = null!;
        _rented = false;
    }
}

/// <summary>
/// <see cref="IBufferWriter{T}"/> へ直接書き込む writer。終了時に <see cref="Flush"/> を呼ぶこと。
/// 書き込み先は参照で持つので、構造体の <see cref="IBufferWriter{T}"/> でも呼び出し元の変数に状態が反映される。
/// </summary>
public ref struct ApexBufferWriterAdapter<TBufferWriter> : IApexBufferWriter
    where TBufferWriter : IBufferWriter<byte>
{
    private ref TBufferWriter _target;
    private Span<byte> _current;
    private int _pending;
    private int _committed;

    public static bool IsHostLittleEndian => BitConverter.IsLittleEndian;

    public ApexBufferWriterAdapter(ref TBufferWriter target)
    {
        _target = ref target;
        _current = default;
        _pending = 0;
        _committed = 0;
    }

    public readonly int BytesWritten => _committed + _pending;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref byte GetSpanRef(int size)
    {
        ref byte p = ref Reserve(size);
        _pending += size;
        return ref p;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref byte Reserve(int maxSize)
    {
        if ((uint)maxSize > (uint)(_current.Length - _pending))
        {
            Acquire(maxSize);
        }
        return ref Unsafe.Add(ref MemoryMarshal.GetReference(_current), _pending);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Advance(int count) => _pending += count;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void Acquire(int size)
    {
        if (size < 0) throw new ArgumentOutOfRangeException(nameof(size));
        Flush();
        _current = _target.GetSpan(Math.Max(size, 4096));
    }

    /// <summary>書き込み済みの領域を下位の <see cref="IBufferWriter{T}"/> へ確定する。</summary>
    public void Flush()
    {
        if (_pending > 0)
        {
            _target.Advance(_pending);
            _committed += _pending;
        }
        _pending = 0;
        _current = default;
    }
}
