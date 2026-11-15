using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ApexPack.Buffers;

/// <summary>
/// 自動伸長する writer。初期バッファを外部から受け取れ(スレッドローカルの再利用)、
/// 足りなくなったら書き終えたバッファはそのまま残して次のバッファ(倍の大きさ、<see cref="ArrayPool{T}"/> から)へ進む。
/// 伸長のたびにそれまでの内容を写さないので、大きな出力でも写しは最後の <see cref="ToArray"/> の 1 回だけになる。
/// 借りた配列は <see cref="Dispose"/> で返す。
/// </summary>
public ref struct ApexBufferWriter : IApexBufferWriter, IDisposable
{
    private byte[] _buffer;
    private int _written;
    private bool _rented;

    /// <summary>書き終えたバッファ(先頭から順)。伸長したときだけ作る。</summary>
    private List<Segment>? _segments;
    private int _segmentBytes;

    private readonly record struct Segment(byte[] Array, int Length, bool Rented);

    public static bool IsHostLittleEndian => BitConverter.IsLittleEndian;

    public ApexBufferWriter(int initialCapacity = 256)
    {
        _buffer = ArrayPool<byte>.Shared.Rent(Math.Max(initialCapacity, 64));
        _written = 0;
        _rented = true;
        _segments = null;
        _segmentBytes = 0;
    }

    /// <summary>呼び出し側が所有する初期バッファで開始する。この配列はプールへ返さない。</summary>
    public ApexBufferWriter(byte[] initialBuffer)
    {
        _buffer = initialBuffer;
        _written = 0;
        _rented = false;
        _segments = null;
        _segmentBytes = 0;
    }

    public readonly int BytesWritten => _segmentBytes + _written;

    /// <summary>書いた内容。伸長して複数のバッファにまたがる場合は、つなげた配列を作って返す。</summary>
    public readonly ReadOnlySpan<byte> WrittenSpan => _segments is null ? _buffer.AsSpan(0, _written) : ToArray();

    /// <summary>書いた内容。伸長して複数のバッファにまたがる場合は、つなげた配列を作って返す。</summary>
    public readonly ReadOnlyMemory<byte> WrittenMemory => _segments is null ? _buffer.AsMemory(0, _written) : ToArray();

    public readonly byte[] ToArray()
    {
        int total = BytesWritten;
        if (total == 0) return Array.Empty<byte>();
        var array = GC.AllocateUninitializedArray<byte>(total);
        ref byte destination = ref MemoryMarshal.GetArrayDataReference(array);
        int offset = 0;
        if (_segments is not null)
        {
            foreach (var segment in _segments)
            {
                Unsafe.CopyBlockUnaligned(ref Unsafe.Add(ref destination, offset), ref MemoryMarshal.GetArrayDataReference(segment.Array), (uint)segment.Length);
                offset += segment.Length;
            }
        }
        if (_written > 0)
        {
            Unsafe.CopyBlockUnaligned(ref Unsafe.Add(ref destination, offset), ref MemoryMarshal.GetArrayDataReference(_buffer), (uint)_written);
        }
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
        long required = (long)BytesWritten + (uint)size;
        if (size < 0 || required > Array.MaxLength)
        {
            throw new InvalidOperationException($"Serialized payload exceeds the maximum array length ({required} bytes).");
        }

        // 今のバッファは書いた分だけ残し、次のバッファへ進む(それまでの内容は写さない)
        if (_written > 0)
        {
            (_segments ??= new List<Segment>()).Add(new Segment(_buffer, _written, _rented));
            _segmentBytes += _written;
        }
        else if (_rented)
        {
            ArrayPool<byte>.Shared.Return(_buffer);
        }

        int newCapacity = (int)Math.Min(Math.Max((long)_buffer.Length * 2, size), Array.MaxLength);
        _buffer = ArrayPool<byte>.Shared.Rent(newCapacity);
        _written = 0;
        _rented = true;
    }

    public void Dispose()
    {
        if (_segments is not null)
        {
            foreach (var segment in _segments)
            {
                if (segment.Rented) ArrayPool<byte>.Shared.Return(segment.Array);
            }
            _segments = null;
            _segmentBytes = 0;
        }
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
