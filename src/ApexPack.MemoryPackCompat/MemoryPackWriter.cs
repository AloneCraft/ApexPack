using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ApexPack;
using ApexPack.Buffers;

namespace MemoryPack;

/// <summary>
/// MemoryPack と同じ使い方の低レベル writer。<see cref="IApexBufferWriter"/> を実装するので、
/// <see cref="WriteValue{T}(in T)"/> などは ApexPack のフォーマッタでそのまま書く(ワイヤ形式は ApexPack)。
/// </summary>
/// <remarks>
/// 下位の <see cref="IBufferWriter{T}"/> には要求された大きさをそのまま渡す(MemoryPack と同じ。下位の writer が実際の大きさを決める)。
/// </remarks>
public ref struct MemoryPackWriter<TBufferWriter> : IApexBufferWriter
    where TBufferWriter : IBufferWriter<byte>
{
    private ref TBufferWriter _bufferWriter;
    private Span<byte> _current;
    private int _pending;
    private int _committed;
    private readonly MemoryPackWriterOptionalState _optionalState;

    public MemoryPackWriter(ref TBufferWriter writer, MemoryPackWriterOptionalState? optionalState)
    {
        _bufferWriter = ref writer;
        _current = default;
        _pending = 0;
        _committed = 0;
        _optionalState = optionalState ?? MemoryPackWriterOptionalState.NullState;
    }

    public static bool IsHostLittleEndian => BitConverter.IsLittleEndian;

    public readonly MemoryPackWriterOptionalState OptionalState => _optionalState;

    public readonly MemoryPackSerializerOptions Options => _optionalState.Options;

    /// <summary>書き込んだバイト数(確定済み + 未確定)。</summary>
    public readonly int WrittenCount => _committed + _pending;

    public readonly int BytesWritten => _committed + _pending;

    /// <summary>
    /// <paramref name="sizeHint"/> バイト以上の書き込み先を返す(位置は進めない)。続けて <see cref="Advance"/> で確定する。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref byte GetSpanReference(int sizeHint)
    {
        if ((uint)sizeHint > (uint)(_current.Length - _pending))
        {
            RequestNewBuffer(sizeHint);
        }
        return ref Unsafe.Add(ref MemoryMarshal.GetReference(_current), _pending);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void RequestNewBuffer(int sizeHint)
    {
        if (sizeHint < 0) throw new ArgumentOutOfRangeException(nameof(sizeHint));
        Flush();
        _current = _bufferWriter.GetSpan(sizeHint);
        if (_current.Length < sizeHint) MemoryPackSerializationException.ThrowInsufficientBufferUnless(sizeHint);
    }

    /// <summary>直前の <see cref="GetSpanReference"/> で得た領域のうち <paramref name="count"/> バイトを確定する。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Advance(int count)
    {
        if ((uint)count > (uint)(_current.Length - _pending))
        {
            MemoryPackSerializationException.ThrowInvalidAdvance();
        }
        _pending += count;
    }

    /// <summary>未確定の書き込みを下位の <see cref="IBufferWriter{T}"/> へ確定する。</summary>
    public void Flush()
    {
        if (_pending > 0)
        {
            _bufferWriter.Advance(_pending);
            _committed += _pending;
        }
        _pending = 0;
        _current = default;
    }

    // ---- IApexBufferWriter ----

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref byte GetSpanRef(int size)
    {
        ref byte p = ref GetSpanReference(size);
        _pending += size;
        return ref p;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref byte Reserve(int maxSize) => ref GetSpanReference(maxSize);

    // ---- 値の書き込み(ApexPack 形式) ----

    /// <summary>任意の型の値を ApexPack のフォーマッタで書く。</summary>
    public void WriteValue<T>(scoped in T? value)
    {
        var tracker = new NullReferenceTracker();
        CachedFormatter<T>.Write(ref this, in value!, ref tracker);
    }

    /// <summary>既知のスカラー型(数値、enum、日時、Guid など)を正規形(リトルエンディアン)で書く。</summary>
    public void WriteUnmanaged<T1>(scoped in T1 value1)
        where T1 : unmanaged
        => ApexBufferExtensions.WriteUnmanaged(ref this, value1);

    public void WriteUnmanaged<T1, T2>(scoped in T1 value1, scoped in T2 value2)
        where T1 : unmanaged
        where T2 : unmanaged
    {
        ApexBufferExtensions.WriteUnmanaged(ref this, value1);
        ApexBufferExtensions.WriteUnmanaged(ref this, value2);
    }

    public void WriteString(string? value) => ApexBufferExtensions.WriteString(ref this, value);

    public void WriteUnmanagedArray<T>(T[]? value)
        where T : unmanaged
        => WriteValue(value);

    public void WriteArray<T>(T?[]? value) => WriteValue(value);

    // ---- ヘッダー(ApexPack 形式: null は 0、それ以外は 数 + 1) ----

    /// <summary>オブジェクトヘッダー(メンバー数 + 1)を書く。メンバー数は 254 まで。</summary>
    public void WriteObjectHeader(byte memberCount)
    {
        if (memberCount >= MemoryPackCode.Reserved1) MemoryPackSerializationException.ThrowWriteInvalidMemberCount(memberCount);
        ApexLowLevelExtensions.WriteObjectHeader(ref this, memberCount);
    }

    public void WriteNullObjectHeader() => ApexLowLevelExtensions.WriteNullObjectHeader(ref this);

    public void WriteCollectionHeader(int length) => ApexBufferExtensions.WriteCollectionHeader(ref this, length);

    public void WriteNullCollectionHeader() => ApexBufferExtensions.WriteNullCollectionHeader(ref this);

    // ---- 可変長整数(ApexPack 形式: ZigZag + 7 ビット単位) ----
    // どの型の値も同じ符号つき形式で書く(MemoryPack と同じく、書いた型と違う型の ReadVarIntXxx でも読める)。ulong は long として書く。

    public void WriteVarInt(byte x) => ApexLowLevelExtensions.WriteVarInt(ref this, x);

    public void WriteVarInt(sbyte x) => ApexLowLevelExtensions.WriteVarInt(ref this, x);

    public void WriteVarInt(ushort x) => ApexLowLevelExtensions.WriteVarInt(ref this, x);

    public void WriteVarInt(short x) => ApexLowLevelExtensions.WriteVarInt(ref this, x);

    public void WriteVarInt(uint x) => ApexLowLevelExtensions.WriteVarInt(ref this, x);

    public void WriteVarInt(int x) => ApexLowLevelExtensions.WriteVarInt(ref this, x);

    public void WriteVarInt(ulong x) => ApexLowLevelExtensions.WriteVarInt(ref this, unchecked((long)x));

    public void WriteVarInt(long x) => ApexLowLevelExtensions.WriteVarInt(ref this, x);
}

/// <summary>参照の記録(MemoryPack の CircularReference 用の状態と同じ使い方)。</summary>
public sealed class MemoryPackWriterOptionalState : IDisposable
{
    internal static readonly MemoryPackWriterOptionalState NullState = new();

    private readonly Dictionary<object, uint> _references = new(ReferenceEqualityComparer.Instance);
    private uint _nextId;

    internal MemoryPackWriterOptionalState() => Options = MemoryPackSerializerOptions.Default;

    public MemoryPackSerializerOptions Options { get; private set; }

    internal void Init(MemoryPackSerializerOptions? options) => Options = options ?? MemoryPackSerializerOptions.Default;

    public void Reset()
    {
        _references.Clear();
        _nextId = 0;
        Options = MemoryPackSerializerOptions.Default;
    }

    public (bool existsReference, uint id) GetOrAddReference(object value)
    {
        if (_references.TryGetValue(value, out var id)) return (true, id);
        id = _nextId++;
        _references.Add(value, id);
        return (false, id);
    }

    void IDisposable.Dispose() => Reset();
}

public static class MemoryPackWriterOptionalStatePool
{
    public static MemoryPackWriterOptionalState Rent(MemoryPackSerializerOptions? options)
    {
        var state = new MemoryPackWriterOptionalState();
        state.Init(options);
        return state;
    }
}
