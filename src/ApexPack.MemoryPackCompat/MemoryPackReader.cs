using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ApexPack;
using ApexPack.Buffers;

namespace MemoryPack;

/// <summary>
/// MemoryPack と同じ使い方の低レベル reader。連続した領域と複数セグメントの <see cref="ReadOnlySequence{T}"/> の両方を読む。
/// <see cref="IApexBufferReader"/> を実装するので、<see cref="ReadValue{T}()"/> などは ApexPack のフォーマッタで読む。
/// </summary>
/// <remarks>
/// セグメントをまたぐ <see cref="GetSpanReference"/> は、要求されたバイト数を一時配列へ写して返す(MemoryPack と同じ考え方)。
/// 範囲外の読み取りは <see cref="MemoryPackSerializationException"/>。
/// </remarks>
public ref struct MemoryPackReader : IApexBufferReader, IDisposable
{
    private readonly ReadOnlySequence<byte> _sequence; // 連続した領域から作ったときは空
    private readonly bool _isSequence;
    private readonly long _length;
    private ReadOnlySpan<byte> _current;
    private int _position;
    private long _consumed;
    private readonly MemoryPackReaderOptionalState _optionalState;

    public MemoryPackReader(in ReadOnlySequence<byte> sequence, MemoryPackReaderOptionalState? optionalState)
    {
        _sequence = sequence.IsSingleSegment ? default : sequence;
        _isSequence = !sequence.IsSingleSegment;
        _length = sequence.Length;
        _current = sequence.FirstSpan;
        _position = 0;
        _consumed = 0;
        _optionalState = optionalState ?? MemoryPackReaderOptionalState.NullState;
        SkipEmptySegments();
    }

    public MemoryPackReader(ReadOnlySpan<byte> buffer, MemoryPackReaderOptionalState? optionalState)
    {
        _sequence = default;
        _isSequence = false;
        _length = buffer.Length;
        _current = buffer;
        _position = 0;
        _consumed = 0;
        _optionalState = optionalState ?? MemoryPackReaderOptionalState.NullState;
    }

    public static bool IsHostLittleEndian => BitConverter.IsLittleEndian;

    public readonly MemoryPackReaderOptionalState OptionalState => _optionalState;

    public readonly MemoryPackSerializerOptions Options => _optionalState.Options;

    public readonly int Consumed => (int)_consumed;

    public readonly long Remaining => _length - _consumed;

    readonly int IApexBufferReader.Remaining => (int)Math.Min(_length - _consumed, int.MaxValue);

    /// <summary>
    /// 現在位置から <paramref name="sizeHint"/> バイトを読める参照を返す(位置は進めない)。続けて <see cref="Advance"/> で進める。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref byte GetSpanReference(int sizeHint)
    {
        if ((uint)sizeHint <= (uint)(_current.Length - _position))
        {
            return ref Unsafe.Add(ref MemoryMarshal.GetReference(_current), _position);
        }
        return ref GetSpanReferenceSlow(sizeHint);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private readonly ref byte GetSpanReferenceSlow(int sizeHint)
    {
        if (sizeHint < 0 || sizeHint > _length - _consumed)
        {
            MemoryPackSerializationException.ThrowSequenceReachedEnd();
        }
        // セグメントをまたぐ: 一時配列へ写す
        var temp = new byte[sizeHint];
        _sequence.Slice(_consumed, sizeHint).CopyTo(temp);
        return ref MemoryMarshal.GetArrayDataReference(temp);
    }

    /// <summary>読み取り位置を進める。</summary>
    public void Advance(int count)
    {
        if (count < 0 || count > _length - _consumed)
        {
            MemoryPackSerializationException.ThrowSequenceReachedEnd();
        }
        _consumed += count;
        if (count <= _current.Length - _position)
        {
            _position += count;
            if (_position == _current.Length) MoveToNextSegment();
            return;
        }
        // セグメントをまたいだ: 残りの先頭セグメントへ移る
        var rest = _sequence.Slice(_consumed);
        _current = rest.FirstSpan;
        _position = 0;
        SkipEmptySegments();
    }

    private void MoveToNextSegment()
    {
        if (!_isSequence || _consumed >= _length) return;
        _current = _sequence.Slice(_consumed).FirstSpan;
        _position = 0;
        SkipEmptySegments();
    }

    private void SkipEmptySegments()
    {
        if (!_isSequence) return;
        while (_current.Length == 0 && _consumed < _length)
        {
            var rest = _sequence.Slice(_consumed);
            foreach (var segment in rest)
            {
                if (segment.Length > 0)
                {
                    _current = segment.Span;
                    break;
                }
            }
            _position = 0;
        }
    }

    /// <summary>
    /// 残りの入力を返す。残りが今のセグメントだけなら <paramref name="singleSource"/>、複数セグメントにまたがるなら <paramref name="remainingSource"/>。
    /// </summary>
    public readonly void GetRemainingSource(out ReadOnlySpan<byte> singleSource, out ReadOnlySequence<byte> remainingSource)
    {
        long rest = _length - _consumed;
        if (_current.Length - _position == rest)
        {
            singleSource = _current.Slice(_position);
            remainingSource = default;
            return;
        }
        singleSource = default;
        remainingSource = _sequence.Slice(_consumed);
    }

    public void Dispose()
    {
    }

    // ---- IApexBufferReader ----

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref readonly byte GetSpanRef(int size)
    {
        ref byte p = ref GetSpanReference(size);
        Advance(size);
        return ref p;
    }

    public readonly byte PeekByte()
    {
        if (_position < _current.Length) return _current[_position];
        MemoryPackSerializationException.ThrowSequenceReachedEnd();
        return 0;
    }

    // ---- 値の読み取り(ApexPack 形式) ----

    public T? ReadValue<T>()
    {
        var tracker = new NullReferenceTracker();
        var guard = new TrustedSecurityGuard();
        return ApexFormatterCache<T>.Instance.Read(ref this, ref tracker, ref guard);
    }

    /// <summary>
    /// 値を読む。<paramref name="value"/> に既存のインスタンスがある参照型は、MemoryPack では上書きするが ApexPack は対象外。
    /// </summary>
    public void ReadValue<T>(scoped ref T? value)
    {
        if (!typeof(T).IsValueType && value is not null) ApexCompatThrow.Overwrite();
        value = ReadValue<T>();
    }

    public T1 ReadUnmanaged<T1>()
        where T1 : unmanaged
        => ApexBufferExtensions.ReadUnmanaged<MemoryPackReader, T1>(ref this);

    public void ReadUnmanaged<T1>(out T1 value1)
        where T1 : unmanaged
        => value1 = ReadUnmanaged<T1>();

    public void ReadUnmanaged<T1, T2>(out T1 value1, out T2 value2)
        where T1 : unmanaged
        where T2 : unmanaged
    {
        value1 = ReadUnmanaged<T1>();
        value2 = ReadUnmanaged<T2>();
    }

    public string? ReadString() => ApexBufferExtensions.ReadString(ref this);

    public T?[]? ReadArray<T>() => ReadValue<T?[]>();

    public T[]? ReadUnmanagedArray<T>()
        where T : unmanaged
        => ReadValue<T[]>();

    // ---- ヘッダー(ApexPack 形式) ----

    public bool PeekIsNull() => PeekByte() == 0;

    public bool TryReadObjectHeader(out byte memberCount) => ApexLowLevelExtensions.TryReadObjectHeader(ref this, out memberCount);

    /// <summary>コレクションの件数ヘッダーを読む。null なら false。残りのバイト数より多い件数は例外。</summary>
    public bool TryReadCollectionHeader(out int length)
    {
        uint header = ApexBufferExtensions.ReadCompactUInt(ref this);
        if (header == 0)
        {
            length = 0;
            return false;
        }
        long count = header - 1L;
        if (count > Remaining) MemoryPackSerializationException.ThrowInsufficientBufferUnless((int)Math.Min(count, int.MaxValue));
        length = (int)count;
        return true;
    }

    // ---- 可変長整数(ApexPack 形式) ----

    public byte ReadVarIntByte() => checked((byte)ApexLowLevelExtensions.ReadVarInt(ref this));

    public sbyte ReadVarIntSByte() => checked((sbyte)ApexLowLevelExtensions.ReadVarInt(ref this));

    public ushort ReadVarIntUInt16() => checked((ushort)ApexLowLevelExtensions.ReadVarInt(ref this));

    public short ReadVarIntInt16() => checked((short)ApexLowLevelExtensions.ReadVarInt(ref this));

    public uint ReadVarIntUInt32() => checked((uint)ApexLowLevelExtensions.ReadVarInt(ref this));

    public int ReadVarIntInt32() => checked((int)ApexLowLevelExtensions.ReadVarInt(ref this));

    public ulong ReadVarIntUInt64() => unchecked((ulong)ApexLowLevelExtensions.ReadVarInt(ref this));

    public long ReadVarIntInt64() => ApexLowLevelExtensions.ReadVarInt(ref this);
}

/// <summary>参照の記録(MemoryPack の CircularReference 用の状態と同じ使い方)。</summary>
public sealed class MemoryPackReaderOptionalState : IDisposable
{
    internal static readonly MemoryPackReaderOptionalState NullState = new();

    private readonly Dictionary<uint, object> _references = new();

    internal MemoryPackReaderOptionalState() => Options = MemoryPackSerializerOptions.Default;

    public MemoryPackSerializerOptions Options { get; private set; }

    internal void Init(MemoryPackSerializerOptions? options) => Options = options ?? MemoryPackSerializerOptions.Default;

    public object GetObjectReference(uint id)
        => _references.TryGetValue(id, out var value) ? value : throw new MemoryPackSerializationException($"Object is not found in this reference id: {id}");

    public void AddObjectReference(uint id, object value)
    {
        if (!_references.TryAdd(id, value)) throw new MemoryPackSerializationException($"Object is already added, id: {id}");
    }

    public void Reset()
    {
        _references.Clear();
        Options = MemoryPackSerializerOptions.Default;
    }

    void IDisposable.Dispose() => Reset();
}

public static class MemoryPackReaderOptionalStatePool
{
    public static MemoryPackReaderOptionalState Rent(MemoryPackSerializerOptions? options)
    {
        var state = new MemoryPackReaderOptionalState();
        state.Init(options);
        return state;
    }
}
